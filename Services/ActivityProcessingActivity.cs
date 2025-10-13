using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text.Json;
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
    private readonly HttpClient _httpClient;

    // --- Configuration Constants (You can fine-tune these) ---
    private const double STOP_CLUSTER_RADIUS_METERS = 50; // Points within 50m are part of a potential stop
    private const int MIN_STOP_DURATION_MINUTES = 1;      // Must stay for at least 1 minute to be a "stop"
    private const string MAPBOX_API_KEY = "pk.eyJ1IjoiYmFzaWwxLTIzIiwiYSI6ImNtZWFvNW43ZTA0ejQycHBtd3dkMHJ1bnkifQ.Y-IlM-vQAlaGr7pVQnug3Q"; // Your Mapbox key

     public ActivityProcessingService(ILogger<ActivityProcessingService> logger, IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _httpClient = httpClientFactory.CreateClient(); // NEW: Create the client
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

        var engineerIds = await context.LocationPoints
            .Select(p => p.FieldEngineerId)
            .Distinct()
            .ToListAsync();

        foreach (var engineerId in engineerIds)
        {
            _logger.LogInformation($"Checking points for engineer {engineerId}");

            var points = await context.LocationPoints
                .Where(p => p.FieldEngineerId == engineerId)
                .OrderBy(p => p.Timestamp) // This is critical
                .ToListAsync();

            if (points.Count < 2) continue;
            
            _logger.LogInformation($"Found {points.Count} points to process for engineer {engineerId}.");

            var newEvents = new List<ActivityEvent>();
            var allStops = new List<ActivityEvent>();

            // --- NEW, SIMPLER ALGORITHM ---

            // PASS 1: Find all the "Stops" first.
            var currentCluster = new List<LocationPoint> { points.First() };
            for (int i = 1; i < points.Count; i++)
            {
                var currentPoint = points[i];
                var lastPointInCluster = currentCluster.Last();
                var distance = CalculateDistance(lastPointInCluster, currentPoint);

                if (distance <= STOP_CLUSTER_RADIUS_METERS)
                {
                    currentCluster.Add(currentPoint);
                }
                else
                {
                    // Cluster ended, check if it was a valid stop
                    var clusterDuration = currentCluster.Last().Timestamp - currentCluster.First().Timestamp;
                    if (clusterDuration.TotalMinutes >= MIN_STOP_DURATION_MINUTES)
                    {
                        var stopEvent = await CreateStopEvent(currentCluster, engineerId);
                        allStops.Add(stopEvent);
                    }
                    currentCluster = new List<LocationPoint> { currentPoint };
                }
            }
            // Process the final cluster after the loop
            if (currentCluster.Count > 1) {
                var clusterDuration = currentCluster.Last().Timestamp - currentCluster.First().Timestamp;
                if (clusterDuration.TotalMinutes >= MIN_STOP_DURATION_MINUTES)
                {
                    var stopEvent = await CreateStopEvent(currentCluster, engineerId);
                    allStops.Add(stopEvent);
                }
            }

            newEvents.AddRange(allStops);
            _logger.LogInformation($"Pass 1 complete. Found {allStops.Count} stops for engineer {engineerId}.");

            // PASS 2: Define "Drives" as the points between the stops.
            DateTime lastEventEndTime = points.First().Timestamp;
            
            foreach (var stop in allStops.OrderBy(s => s.StartTime))
            {
                var drivePoints = points.Where(p => p.Timestamp > lastEventEndTime && p.Timestamp < stop.StartTime).ToList();
                if (drivePoints.Count > 1)
                {
                    var driveEvent = await CreateDriveEvent(drivePoints, engineerId);
                    newEvents.Add(driveEvent);
                }
                lastEventEndTime = stop.EndTime;
            }

            // --- END OF NEW ALGORITHM ---
            
            if (newEvents.Any())
            {
                await context.ActivityEvents.AddRangeAsync(newEvents);
            }
            
            // Clean up the processed raw points
            context.LocationPoints.RemoveRange(points);
            await context.SaveChangesAsync();
            
            _logger.LogInformation($"Processing complete. Created {newEvents.Count} new events for engineer {engineerId}.");
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

    private async Task<ActivityEvent> CreateDriveEvent(List<LocationPoint> drivePoints, int engineerId)
    {
        double totalDistanceKm = 0;
        for (int i = 0; i < drivePoints.Count - 1; i++)
        {
            totalDistanceKm += CalculateDistance(drivePoints[i], drivePoints[i+1]) / 1000.0;
        }

        var startPoint = drivePoints.First();
        var endPoint = drivePoints.Last();

        // Geocode start and end addresses
        var (_, startAddress) = await ReverseGeocodeAsync(startPoint.Latitude, startPoint.Longitude);
        var (_, endAddress) = await ReverseGeocodeAsync(endPoint.Latitude, endPoint.Longitude);

        return new ActivityEvent
        {
            FieldEngineerId = engineerId,
            Type = EventType.Drive,
            StartTime = drivePoints.First().Timestamp,
            EndTime = drivePoints.Last().Timestamp,
            DurationMinutes = (int)(drivePoints.Last().Timestamp - drivePoints.First().Timestamp).TotalMinutes,
            DistanceKm = totalDistanceKm,
            TopSpeedKmh = drivePoints.Max(p => p.Speed ?? 0) * 3.6, // Convert m/s to km/h
            StartLatitude = startPoint.Latitude,
            StartLongitude = startPoint.Longitude,
            StartAddress = startAddress,
            EndLatitude = endPoint.Latitude,
            EndLongitude = endPoint.Longitude,
            EndAddress = endAddress,
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
    // The URL for the Mapbox Geocoding API
    var url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lng},{lat}.json?types=poi,address&access_token={MAPBOX_API_KEY}";

    try
    {
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError($"Mapbox API error: {response.StatusCode}");
            return ($"Location near {lat:F3}, {lng:F3}", "Address lookup failed");
        }

        var jsonString = await response.Content.ReadAsStringAsync();
        using (var jsonDoc = JsonDocument.Parse(jsonString))
        {
            var features = jsonDoc.RootElement.GetProperty("features");

            if (features.GetArrayLength() > 0)
            {
                // Get the first, most relevant result
                var firstFeature = features[0];
                string placeName = firstFeature.GetProperty("text").GetString() ?? "Unknown Place";
                string fullAddress = firstFeature.GetProperty("place_name").GetString() ?? "Address not available";
                
                _logger.LogInformation($"Geocoded ({lat},{lng}) to: {placeName}");
                return (placeName, fullAddress);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"Exception during reverse geocoding for ({lat},{lng})");
    }

    // Fallback if anything goes wrong
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