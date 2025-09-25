using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class ActivityProcessingService : IHostedService, IDisposable
{
    private readonly ILogger<ActivityProcessingService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private Timer? _timer;

    // --- Configuration Constants (You can fine-tune these) ---
    private const double STOP_CLUSTER_RADIUS_METERS = 50; // Points within 50m are part of a potential stop
    private const int MIN_STOP_DURATION_MINUTES = 1;      // Must stay for at least 1 minute to be a "stop"

    public ActivityProcessingService(ILogger<ActivityProcessingService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Activity Processing Service is starting.");
        // Run the processor every 15 minutes
        _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(15));
        return Task.CompletedTask;
    }

    private void DoWork(object? state)
    {
        _logger.LogInformation("Activity Processing Service is running.");
        Task.Run(async () => await ProcessActivities());
    }

    private async Task ProcessActivities()
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            _logger.LogInformation("Processing raw location points...");

            // Get all engineers who have unprocessed points
            var engineerIds = await context.LocationPoints
                .Select(p => p.FieldEngineerId)
                .Distinct()
                .ToListAsync();

            foreach (var engineerId in engineerIds)
            {
                // Get all points for this engineer, ordered by time
                 _logger.LogInformation($"Processing points for engineer {engineerId}"); 
                var points = await context.LocationPoints
                    .Where(p => p.FieldEngineerId == engineerId)
                    // ...
                    .ToListAsync();

                if (points.Count < 2) continue;

                // --- This is the core algorithm for detecting stops and drives ---
                ActivityEvent? lastStop = null;
                var currentCluster = new List<LocationPoint> { points.First() };

                for (int i = 1; i < points.Count; i++)
                {
                    var currentPoint = points[i];
                    var lastPointInCluster = currentCluster.Last();

                    var distance = CalculateDistance(lastPointInCluster, currentPoint);

                    if (distance <= STOP_CLUSTER_RADIUS_METERS)
                    {
                        // Point is close to the cluster, add it
                        currentCluster.Add(currentPoint);
                    }
                    else
                    {
                        // Point is far away, the previous cluster has ended.
                        // Let's process the cluster we just finished.
                        var clusterDuration = currentCluster.Last().Timestamp - currentCluster.First().Timestamp;
                        
                        if (clusterDuration.TotalMinutes >= MIN_STOP_DURATION_MINUTES)
                        {
                            // It's a valid stop!
                            var stopEvent = await CreateStopEvent(currentCluster, engineerId);
                            context.ActivityEvents.Add(stopEvent);
                            lastStop = stopEvent;
                        }

                        // The points between the last real stop and this new one form a DRIVE.
                        var drivePoints = points.Where(p => 
                            (lastStop == null || p.Timestamp > lastStop.EndTime) && 
                            p.Timestamp <= currentCluster.First().Timestamp
                        ).ToList();
                        
                        if(drivePoints.Any())
                        {
                            var driveEvent = CreateDriveEvent(drivePoints, engineerId);
                            context.ActivityEvents.Add(driveEvent);
                        }
                        
                        // Start a new cluster with the current point
                        currentCluster = new List<LocationPoint> { currentPoint };
                    }
                }
                
                // After the loop, save all new events and delete the raw points
                await context.SaveChangesAsync();
                context.LocationPoints.RemoveRange(points); // Clean up processed points
                await context.SaveChangesAsync();
            }
        }
    }

    private async Task<ActivityEvent> CreateStopEvent(List<LocationPoint> cluster, int engineerId)
    {
        var startTime = cluster.First().Timestamp;
        var endTime = cluster.Last().Timestamp;
        var avgLat = cluster.Average(p => p.Latitude);
        var avgLng = cluster.Average(p => p.Longitude);

        // TODO: Call a real reverse geocoding service here
        var (locationName, address) = await ReverseGeocodeAsync(avgLat, avgLng);
        
        return new ActivityEvent
        {
            FieldEngineerId = engineerId,
            Type = EventType.Stop,
            StartTime = startTime,
            EndTime = endTime,
            DurationMinutes = (int)(endTime - startTime).TotalMinutes,
            Latitude = avgLat,
            Longitude = avgLng,
            LocationName = locationName,
            Address = address,
        };
    }

    private ActivityEvent CreateDriveEvent(List<LocationPoint> drivePoints, int engineerId)
    {
        double totalDistanceKm = 0;
        for (int i = 0; i < drivePoints.Count - 1; i++)
        {
            totalDistanceKm += CalculateDistance(drivePoints[i], drivePoints[i+1]) / 1000.0;
        }

        return new ActivityEvent
        {
            FieldEngineerId = engineerId,
            Type = EventType.Drive,
            StartTime = drivePoints.First().Timestamp,
            EndTime = drivePoints.Last().Timestamp,
            DurationMinutes = (int)(drivePoints.Last().Timestamp - drivePoints.First().Timestamp).TotalMinutes,
            DistanceKm = totalDistanceKm,
            TopSpeedKmh = drivePoints.Max(p => p.Speed ?? 0) * 3.6, // Convert m/s to km/h
        };
    }

    // --- Helper & External API Methods ---

    private double CalculateDistance(LocationPoint p1, LocationPoint p2)
    {
        var d1 = p1.Latitude * (Math.PI / 180.0);
        var num1 = p1.Longitude * (Math.PI / 180.0);
        var d2 = p2.Latitude * (Math.PI / 180.0);
        var num2 = p2.Longitude * (Math.PI / 180.0) - num1;
        var d3 = Math.Pow(Math.Sin((d2 - d1) / 2.0), 2.0) + Math.Cos(d1) * Math.Cos(d2) * Math.Pow(Math.Sin(num2 / 2.0), 2.0);
        return 6376500.0 * (2.0 * Math.Atan2(Math.Sqrt(d3), Math.Sqrt(1.0 - d3)));
    }
    
    private async Task<(string LocationName, string Address)> ReverseGeocodeAsync(double lat, double lng)
    {
        // In a real app, you would call the Mapbox Geocoding API here.
        // For now, we'll return a placeholder.
        var url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lng},{lat}.json?access_token=pk.eyJ1IjoiYmFzaWwxLTIzIiwiYSI6ImNtZWFvNW43ZTA0ejQycHBtd3dkMHJ1bnkifQ.Y-IlM-vQAlaGr7pVQnug3Q";
        await Task.Delay(100); // Simulate network call
        return ($"Location near {lat:F3}, {lng:F3}", "Address not available");
    }


    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Activity Processing Service is stopping.");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}