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
using System.Collections.Generic;

namespace EcsFeMappingApi.Services
{
    public class ActivityProcessingService : IHostedService, IDisposable
    {
        // Constants for activity detection logic
        private const double STOP_SPEED_THRESHOLD_MS = 1.4; // Speed less than 5 km/h
        private const double MIN_STOP_DURATION_MINUTES = 5;
        private const int MIN_DRIVE_POINTS = 2;

        private readonly ILogger<ActivityProcessingService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory; // Use factory for HttpClient
        private Timer? _timer;

        // CORRECTED CONSTRUCTOR
        public ActivityProcessingService(
            ILogger<ActivityProcessingService> logger,
            IServiceProvider serviceProvider,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Activity Processing Service is starting.");
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(15));
            return Task.CompletedTask;
        }

        private void DoWork(object? state)
        {
            _logger.LogInformation("Activity Processing Service is running via timer.");
            Task.Run(async () => await ProcessActivities());
        }

        public async Task TriggerProcessingAsync()
        {
            _logger.LogInformation("Activity Processing Service is being triggered manually.");
            await ProcessActivities();
        }

        private async Task ProcessActivities()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                _logger.LogInformation("[PROCESS] Starting activity processing...");

                var engineersWithNewPoints = await dbContext.LocationPoints
                    .Where(p => !p.IsProcessed)
                    .Select(p => p.FieldEngineerId)
                    .Distinct()
                    .ToListAsync();

                if (!engineersWithNewPoints.Any())
                {
                    _logger.LogInformation("[PROCESS] No engineers with new points to process. Exiting.");
                    return;
                }

                foreach (var engineerId in engineersWithNewPoints)
                {
                    _logger.LogInformation($"[PROCESS] Processing activities for Engineer ID: {engineerId}");
                    
                    var pointsToProcess = await dbContext.LocationPoints
                        .Where(p => p.FieldEngineerId == engineerId && !p.IsProcessed)
                        .OrderBy(p => p.Timestamp)
                        .ToListAsync();

                    if (!pointsToProcess.Any())
                    {
                        _logger.LogWarning($"[PROCESS] Engineer {engineerId} was in the list but has no unprocessed points. Skipping.");
                        continue;
                    }
                    
                    _logger.LogInformation($"[PROCESS] Found {pointsToProcess.Count} points to process for engineer {engineerId}.");

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
                            if (potentialStopPoints.Any())
                            {
                                var stopDuration = (potentialStopPoints.Last().Timestamp - potentialStopPoints.First().Timestamp).TotalMinutes;
                                _logger.LogInformation($"[PROCESS] Potential stop ended. Duration: {stopDuration} minutes. Points: {potentialStopPoints.Count}");
                                if (stopDuration >= MIN_STOP_DURATION_MINUTES)
                                {
                                    _logger.LogInformation($"[PROCESS] CONFIRMED STOP of {stopDuration} minutes. Creating event.");
                                    var stopEvent = await CreateStopEvent(potentialStopPoints, engineerId, dbContext);
                                    newEvents.Add(stopEvent);
                                }
                                potentialStopPoints.Clear();
                            }
                        }
                    }

                    if (potentialStopPoints.Any())
                    {
                        var stopDuration = (potentialStopPoints.Last().Timestamp - potentialStopPoints.First().Timestamp).TotalMinutes;
                        if (stopDuration >= MIN_STOP_DURATION_MINUTES)
                        {
                            var stopEvent = await CreateStopEvent(potentialStopPoints, engineerId, dbContext);
                            newEvents.Add(stopEvent);
                        }
                    }

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
                            // CORRECTED: Pass dbContext to CreateDriveEvent
                            var driveEvent = await CreateDriveEvent(drivePoints, engineerId, dbContext);
                            newEvents.Add(driveEvent);
                        }
                    }

                    if (newEvents.Any())
                    {
                        await dbContext.ActivityEvents.AddRangeAsync(newEvents);
                    }

                    pointsToProcess.ForEach(p => p.IsProcessed = true);
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation($"[PROCESS] Finished processing for Engineer ID: {engineerId}. Found {newEvents.Count} new activities.");
                }
            }
        }

        private async Task<ActivityEvent> CreateStopEvent(List<LocationPoint> stopPoints, int engineerId, AppDbContext dbContext)
        {
            var firstPoint = stopPoints.First();
            var lastPoint = stopPoints.Last();
            var averageLat = stopPoints.Average(p => p.Latitude);
            var averageLon = stopPoints.Average(p => p.Longitude);

            var (_, address) = await ReverseGeocodeAsync(averageLat, averageLon, dbContext);

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

        // CORRECTED: Added dbContext parameter
        private async Task<ActivityEvent> CreateDriveEvent(List<LocationPoint> drivePoints, int engineerId, AppDbContext dbContext)
        {
            var firstPoint = drivePoints.First();
            var lastPoint = drivePoints.Last();
            double totalDistance = 0;
            for (int i = 0; i < drivePoints.Count - 1; i++)
            {
                totalDistance += HaversineDistance(drivePoints[i], drivePoints[i + 1]);
            }

            var (_, startAddress) = await ReverseGeocodeAsync(firstPoint.Latitude, firstPoint.Longitude, dbContext);
            var (_, endAddress) = await ReverseGeocodeAsync(lastPoint.Latitude, lastPoint.Longitude, dbContext);

            return new ActivityEvent
            {
                FieldEngineerId = engineerId,
                Type = EventType.Drive,
                StartTime = firstPoint.Timestamp,
                EndTime = lastPoint.Timestamp,
                DurationMinutes = (int)(lastPoint.Timestamp - firstPoint.Timestamp).TotalMinutes,
                DistanceKm = totalDistance,
                TopSpeedKmh = drivePoints.Max(p => p.Speed ?? 0) * 3.6, // Convert m/s to km/h
                StartLatitude = firstPoint.Latitude,
                StartLongitude = firstPoint.Longitude,
                EndLatitude = lastPoint.Latitude,
                EndLongitude = lastPoint.Longitude,
                StartAddress = startAddress,
                EndAddress = endAddress
            };
        }

        // CORRECTED: Added dbContext parameter and use _httpClientFactory
       private async Task<(string, string)> ReverseGeocodeAsync(double lat, double lon, AppDbContext dbContext)
        {
            var httpClient = _httpClientFactory.CreateClient();
            var apiKey = "pk.eyJ1IjoiYmFzaWwxLTIzIiwiYSI6ImNsa3ZudnZqZDBpZ2szZHFxZ3NqYjB6d2cifQ.Xb3Jp3a_UKWv3yN4nJ5A7A";
            var url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lon},{lat}.json?access_token={apiKey}";

            try
            {
                _logger.LogInformation($"[GEOCODE] Attempting to geocode coordinates: {lat}, {lon} at URL: {url}");
                
                var response = await httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"[GEOCODE_FAIL] Mapbox API request failed with status code: {response.StatusCode}. Body: {errorBody}");
                    return ("Unknown", "Address lookup failed");
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                using (var jsonDoc = JsonDocument.Parse(jsonString))
                {
                    var features = jsonDoc.RootElement.GetProperty("features");
                    if (features.GetArrayLength() > 0)
                    {
                        var placeName = features[0].GetProperty("place_name").GetString() ?? "Unknown Address";
                        var context = features[0].GetProperty("context");
                        var locality = context.EnumerateArray().FirstOrDefault(c => c.GetProperty("id").GetString().StartsWith("locality")).GetProperty("text").GetString() ?? "Unknown City";
                        
                        _logger.LogInformation($"[GEOCODE_SUCCESS] Geocoding successful: {placeName}");
                        return (locality, placeName);
                    }
                }
                _logger.LogWarning("[GEOCODE_WARN] Geocoding successful but no features found.");
                return ("Unknown", "No address found for coordinates");
            }
            catch (Exception ex)
            {
                // This will now log the exact error message to Railway
                _logger.LogError(ex, "[GEOCODE_CRITICAL] An unhandled exception occurred during the reverse geocoding HTTP request.");
            }
            
            // Return a default value if any error occurs
            return ("Unknown", "Address lookup failed due to an exception");
        }
        private double HaversineDistance(LocationPoint p1, LocationPoint p2)
        {
            var R = 6371; // Radius of Earth in kilometers
            var dLat = (p2.Latitude - p1.Latitude) * (Math.PI / 180);
            var dLon = (p2.Longitude - p1.Longitude) * (Math.PI / 180);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(p1.Latitude * (Math.PI / 180)) * Math.Cos(p2.Latitude * (Math.PI / 180)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
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