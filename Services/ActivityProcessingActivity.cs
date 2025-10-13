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
        private const double MIN_STOP_DURATION_MINUTES = 1;
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
                _logger.LogInformation("Starting activity processing...");

                var engineersWithNewPoints = await dbContext.LocationPoints
                    .Where(p => !p.IsProcessed)
                    .Select(p => p.FieldEngineerId)
                    .Distinct()
                    .ToListAsync();

                foreach (var engineerId in engineersWithNewPoints)
                {
                    _logger.LogInformation($"Processing activities for Engineer ID: {engineerId}");

                    var pointsToProcess = await dbContext.LocationPoints
                        .Where(p => p.FieldEngineerId == engineerId && !p.IsProcessed)
                        .OrderBy(p => p.Timestamp)
                        .ToListAsync();

                    if (!pointsToProcess.Any()) continue;

                    var newEvents = await DetectEvents(pointsToProcess, engineerId, dbContext);

                    if (newEvents.Any())
                    {
                        await dbContext.ActivityEvents.AddRangeAsync(newEvents);
                        _logger.LogInformation($"Adding {newEvents.Count} new events for Engineer ID: {engineerId}.");
                    }

                    pointsToProcess.ForEach(p => p.IsProcessed = true);
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation($"Finished processing for Engineer ID: {engineerId}. Marked {pointsToProcess.Count} points as processed.");
                }
            }
        }

        private async Task<List<ActivityEvent>> DetectEvents(List<LocationPoint> points, int engineerId, AppDbContext dbContext)
        {
            var events = new List<ActivityEvent>();
            if (points.Count == 0) return events;

            var currentSegment = new List<LocationPoint>();
            bool isCurrentlyStopped = (points.First().Speed ?? 0) < STOP_SPEED_THRESHOLD_MS;

            foreach (var point in points)
            {
                bool isStopped = (point.Speed ?? 0) < STOP_SPEED_THRESHOLD_MS;

                if (isStopped == isCurrentlyStopped)
                {
                    currentSegment.Add(point);
                }
                else
                {
                    // The type of movement has changed, so process the completed segment
                    if (currentSegment.Any())
                    {
                        var newEvent = isCurrentlyStopped
                            ? await CreateStopEvent(currentSegment, engineerId, dbContext)
                            : await CreateDriveEvent(currentSegment, engineerId, dbContext);
                        
                        if (newEvent != null) events.Add(newEvent);
                    }

                    // Start a new segment
                    currentSegment = new List<LocationPoint> { point };
                    isCurrentlyStopped = isStopped;
                }
            }

            // Process the last remaining segment
            if (currentSegment.Any())
            {
                var lastEvent = isCurrentlyStopped
                    ? await CreateStopEvent(currentSegment, engineerId, dbContext)
                    : await CreateDriveEvent(currentSegment, engineerId, dbContext);

                if (lastEvent != null) events.Add(lastEvent);
            }

            return events;
        }


        private async Task<ActivityEvent?> CreateStopEvent(List<LocationPoint> stopPoints, int engineerId, AppDbContext dbContext)
        {
            if (!stopPoints.Any()) return null;

            var firstPoint = stopPoints.First();
            var lastPoint = stopPoints.Last();
            var duration = (lastPoint.Timestamp - firstPoint.Timestamp).TotalMinutes;

            if (duration < MIN_STOP_DURATION_MINUTES) return null;

            var averageLat = stopPoints.Average(p => p.Latitude);
            var averageLon = stopPoints.Average(p => p.Longitude);

            var (locationName, address) = await ReverseGeocodeAsync(averageLat, averageLon, dbContext);

            return new ActivityEvent
            {
                FieldEngineerId = engineerId,
                Type = EventType.Stop,
                StartTime = firstPoint.Timestamp,
                EndTime = lastPoint.Timestamp,
                DurationMinutes = (int)duration,
                Address = address,
                LocationName = locationName,
                Latitude = averageLat,
                Longitude = averageLon
            };
        }

        private async Task<ActivityEvent?> CreateDriveEvent(List<LocationPoint> drivePoints, int engineerId, AppDbContext dbContext)
        {
            if (drivePoints.Count < MIN_DRIVE_POINTS) return null;

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
                TopSpeedKmh = drivePoints.Max(p => p.Speed ?? 0) * 3.6,
                StartLatitude = firstPoint.Latitude,
                StartLongitude = firstPoint.Longitude,
                EndLatitude = lastPoint.Latitude,
                EndLongitude = lastPoint.Longitude,
                StartAddress = startAddress,
                EndAddress = endAddress
            };
        }

        private async Task<(string LocationName, string Address)> ReverseGeocodeAsync(double lat, double lon, AppDbContext dbContext)
        {
            var httpClient = _httpClientFactory.CreateClient();
            var apiKey = "pk.eyJ1IjoiYmFzaWwxLTIzIiwiYSI6ImNsa3ZudnZqZDBpZ2szZHFxZ3NqYjB6d2cifQ.Xb3Jp3a_UKWv3yN4nJ5A7A";
            var url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lon},{lat}.json?access_token={apiKey}";

            try
            {
                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    using (var jsonDoc = JsonDocument.Parse(jsonString))
                    {
                        var features = jsonDoc.RootElement.GetProperty("features");
                        if (features.GetArrayLength() > 0)
                        {
                            var feature = features[0];
                            var placeName = feature.TryGetProperty("place_name", out var placeNameProp) ? placeNameProp.GetString() ?? "Unknown Address" : "Unknown Address";
                            
                            string locationName = "Unknown";
                            if (feature.TryGetProperty("context", out var contextProp))
                            {
                                locationName = contextProp.EnumerateArray()
                                    .FirstOrDefault(c => c.TryGetProperty("id", out var idProp) && (idProp.GetString()?.StartsWith("locality") ?? false))
                                    .GetProperty("text").GetString() ?? "Unknown";
                            }
                            
                            return (locationName, placeName);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning($"Reverse geocoding failed with status code {response.StatusCode} for {lat},{lon}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during reverse geocoding.");
            }
            return ("Unknown", "Unknown Address");
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