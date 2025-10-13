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
using System.Collections.Generic; // Add this

namespace EcsFeMappingApi.Services
{
    public class ActivityProcessingService : IHostedService, IDisposable
    {
        // --- ADD THESE CONSTANTS ---
        // If speed is less than 5 km/h (approx 1.4 m/s), we consider it stopped.
        private const double STOP_SPEED_THRESHOLD_MS = 1.4; 
        // A stop must be at least 5 minutes long.
        private const double MIN_STOP_DURATION_MINUTES = 5;
        // A drive must have at least 2 points.
        private const int MIN_DRIVE_POINTS = 2;


        private readonly ILogger<ActivityProcessingService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly HttpClient _httpClient;
        private Timer? _timer;

        
        private const double STOP_CLUSTER_RADIUS_METERS = 50; // Points within 50m are part of a potential stop
             
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
            _logger.LogInformation("Starting activity processing task...");
            Task.Run(async () => await ProcessActivities());
        }

        public Task TriggerProcessingAsync()
        {
            _logger.LogInformation("Manual activity processing triggered.");
            // We don't want to wait for this, so we don't await it.
            _ = Task.Run(async () => await ProcessActivities());
            return Task.CompletedTask;
        }

        private async Task ProcessActivities()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                _logger.LogInformation("Starting activity processing...");

                var engineersWithNewPoints = await dbContext.LocationPoints
                    .Where(p => !p.IsProcessed)
                    .Select(p => p.FieldEngineerId)
                    .Distinct()
                    .ToListAsync();

                foreach (var engineerId in engineersWithNewPoints)
                {
                    _logger.LogInformation($"Processing activities for Engineer ID: {engineerId}");

                    var lastEvent = await dbContext.ActivityEvents
                        .Where(e => e.FieldEngineerId == engineerId)
                        .OrderByDescending(e => e.EndTime)
                        .FirstOrDefaultAsync();
                    
                    var pointsToProcess = await dbContext.LocationPoints
                        .Where(p => p.FieldEngineerId == engineerId && !p.IsProcessed)
                        .OrderBy(p => p.Timestamp)
                        .ToListAsync();

                    if (!pointsToProcess.Any()) continue;

                    var newEvents = new List<ActivityEvent>();
                    var potentialStopPoints = new List<LocationPoint>();

                    // This loop identifies STOPS first
                    foreach (var point in pointsToProcess)
                    {
                        if ((point.Speed ?? 0) < STOP_SPEED_THRESHOLD_MS)
                        {
                            potentialStopPoints.Add(point);
                        }
                        else
                        {
                            // Speed is high, so if we were tracking a potential stop, let's check it.
                            if (potentialStopPoints.Any())
                            {
                                var stopDuration = (potentialStopPoints.Last().Timestamp - potentialStopPoints.First().Timestamp).TotalMinutes;
                                if (stopDuration >= MIN_STOP_DURATION_MINUTES)
                                {
                                    // It's a confirmed stop!
                                    var stopEvent = await CreateStopEvent(potentialStopPoints, engineerId, dbContext);
                                    newEvents.Add(stopEvent);
                                }
                                potentialStopPoints.Clear();
                            }
                        }
                    }

                    // Check for any lingering potential stop at the very end
                    if (potentialStopPoints.Any())
                    {
                        var stopDuration = (potentialStopPoints.Last().Timestamp - potentialStopPoints.First().Timestamp).TotalMinutes;
                        if (stopDuration >= MIN_STOP_DURATION_MINUTES)
                        {
                            var stopEvent = await CreateStopEvent(potentialStopPoints, engineerId, dbContext);
                            newEvents.Add(stopEvent);
                        }
                    }

                    // Now, create DRIVE events for the gaps between stops
                    var allEvents = (lastEvent != null ? new List<ActivityEvent> { lastEvent } : new List<ActivityEvent>())
                        .Concat(newEvents)
                        .OrderBy(e => e.StartTime)
                        .ToList();

                    for (int i = 0; i < allEvents.Count; i++)
                    {
                        var currentEvent = allEvents[i];
                        DateTime driveStartTime = currentEvent.EndTime;
                        DateTime driveEndTime = (i + 1 < allEvents.Count) ? allEvents[i + 1].StartTime : DateTime.MaxValue;

                        var drivePoints = pointsToProcess
                            .Where(p => p.Timestamp > driveStartTime && p.Timestamp < driveEndTime)
                            .ToList();

                        if (drivePoints.Count >= MIN_DRIVE_POINTS)
                        {
                            var driveEvent = await CreateDriveEvent(drivePoints, engineerId);
                            newEvents.Add(driveEvent);
                        }
                    }

                    if (newEvents.Any())
                    {
                        await dbContext.ActivityEvents.AddRangeAsync(newEvents);
                    }

                    // Mark all processed points
                    pointsToProcess.ForEach(p => p.IsProcessed = true);
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation($"Finished processing for Engineer ID: {engineerId}. Found {newEvents.Count} new activities.");
                }
            }
        }

        private async Task<ActivityEvent> CreateStopEvent(List<LocationPoint> stopPoints, int engineerId, AppDbContext dbContext)
        {
            var firstPoint = stopPoints.First();
            var lastPoint = stopPoints.Last();
            var averageLat = stopPoints.Average(p => p.Latitude);
            var averageLon = stopPoints.Average(p => p.Longitude);

            var (_, address) = await ReverseGeocodeAsync(averageLat, averageLon);

            return new ActivityEvent
            {
                FieldEngineerId = engineerId,
                Type = EventType.Stop,
                StartTime = firstPoint.Timestamp,
                EndTime = lastPoint.Timestamp,
                DurationMinutes = (int)(lastPoint.Timestamp - firstPoint.Timestamp).TotalMinutes,
                Address = address,
                Latitude = averageLat,
                Longitude = averageLon
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
}