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
using Microsoft.AspNetCore.StaticAssets;

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
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(2));
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

                    _logger.LogInformation($"⚙️ Found {engineersWithNewPoints.Count} engineers with unprocessed points.");

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

                    // ✅ Leave a trailing window unprocessed so stops/drives can accumulate across runs
                    // Use at least MIN_STOP_DURATION_MIN as window; keep newest points so next run can extend the stop/drive
                    var nowUtc = DateTime.UtcNow;
                    var windowMinutes = Math.Max(2, 5); // keep 5 minutes (or your MIN_STOP_DURATION_MIN)
                    var cutoff = nowUtc.AddMinutes(-windowMinutes);

                    int kept = 0, marked = 0;
                    foreach (var p in pointsToProcess)
                    {
                        if (p.Timestamp <= cutoff)
                        {
                            p.IsProcessed = true;
                            marked++;
                        }
                        else
                        {
                            // keep recent points unprocessed
                            kept++;
                        }
                    }

                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation($"Finished processing for Engineer ID: {engineerId}. Marked {marked} points as processed, kept {kept} recent points for accumulation.");
                }
            }
        }

        private async Task<List<ActivityEvent>> DetectEvents(List<LocationPoint> locationPoints, int engineerId, AppDbContext dbContext)
        {
            var events = new List<ActivityEvent>();
            if (locationPoints.Count == 0) return events;

            // 🔧 TEMPORARILY LOWERED THRESHOLDS for easier testing
            const double MOVE_SPEED_THRESHOLD_KMH = 4.0;   // was 8.0
            const double STOP_SPEED_THRESHOLD_KMH = 1.0;   // was 3.0
            const double MIN_TRIP_DISTANCE_KM = 0.05;      // was 0.1 (50 meters)
            const int MIN_STOP_DURATION_MIN = 2;           // was 5
            const int MIN_TRIP_DURATION_MIN = 1;
            const double STAY_RADIUS_METERS = 50;          // was 100

            var currentDrivePoints = new List<LocationPoint>();
            ActivityEvent? lastStopEvent = null;
            bool isMoving = false;

            // ✅ Iterate through all points chronologically
            for (int i = 0; i < locationPoints.Count; i++)
            {
                var point = locationPoints[i];

                // ✅ Auto-compute speed if missing or 0
                double speedKmh;
                if (point.Speed == null || point.Speed == 0)
                {
                    if (i > 0)
                    {
                        var prev = locationPoints[i - 1];
                        var distKm = HaversineDistance(prev, point);
                        var timeHr = (point.Timestamp - prev.Timestamp).TotalHours;
                        speedKmh = (timeHr > 0 ? distKm / timeHr : 0);
                    }
                    else
                    {
                        speedKmh = 0;
                    }
                }
                else
                {
                    speedKmh = (point.Speed ?? 0) * 3.6;
                }

                // 🧠 Debug logging for visibility
                _logger.LogInformation($"[{engineerId}] Point: {point.Timestamp:HH:mm:ss} | Speed={speedKmh:F1} km/h | Lat={point.Latitude:F5}, Lon={point.Longitude:F5}");

                if (speedKmh > MOVE_SPEED_THRESHOLD_KMH)
                {
                    // 🚗 Moving
                    isMoving = true;
                    currentDrivePoints.Add(point);
                }
                else
                {
                    // 🅿️ Stopped or slow
                    if (isMoving && currentDrivePoints.Count > 1)
                    {
                        double tripDistance = 0;
                        for (int j = 0; j < currentDrivePoints.Count - 1; j++)
                            tripDistance += HaversineDistance(currentDrivePoints[j], currentDrivePoints[j + 1]);

                        var tripDuration = (currentDrivePoints.Last().Timestamp - currentDrivePoints.First().Timestamp).TotalMinutes;

                        if (tripDistance >= MIN_TRIP_DISTANCE_KM && tripDuration >= MIN_TRIP_DURATION_MIN)
                        {
                            var driveEvent = await CreateDriveEvent(currentDrivePoints, engineerId, dbContext);
                            if (driveEvent != null)
                            {
                                _logger.LogInformation($"🚗 DRIVE DETECTED: {tripDistance:F2} km, {tripDuration:F1} min");
                                events.Add(driveEvent);
                            }
                        }

                        currentDrivePoints.Clear();
                    }

                    isMoving = false;

                    // 🕓 Detect or extend stop event
                    if (lastStopEvent == null)
                    {
                        lastStopEvent = new ActivityEvent
                        {
                            FieldEngineerId = engineerId,
                            Type = EventType.Stop,
                            StartTime = point.Timestamp.ToUniversalTime(),
                            StartLatitude = point.Latitude,
                            StartLongitude = point.Longitude,
                            EndTime = point.Timestamp.ToUniversalTime(),
                        };
                    }
                    else
                    {
                        var distFromLast = HaversineDistance(
                            new LocationPoint { Latitude = point.Latitude, Longitude = point.Longitude },
                            new LocationPoint { Latitude = lastStopEvent.StartLatitude ?? 0, Longitude = lastStopEvent.StartLongitude ?? 0 }
                        ) * 1000.0; // meters

                        if (distFromLast <= STAY_RADIUS_METERS)
                        {
                            lastStopEvent.EndTime = point.Timestamp;
                        }
                        else
                        {
                            var stayDuration = (lastStopEvent.EndTime - lastStopEvent.StartTime).TotalMinutes;
                            if (stayDuration >= MIN_STOP_DURATION_MIN)
                            {
                                lastStopEvent.DurationMinutes = (int)stayDuration;
                                _logger.LogInformation($"⏸ STOP DETECTED: {stayDuration:F1} min stay within {STAY_RADIUS_METERS}m");
                                events.Add(lastStopEvent);
                            }

                            lastStopEvent = new ActivityEvent
                            {
                                FieldEngineerId = engineerId,
                                Type = EventType.Stop,
                                StartTime = point.Timestamp,
                                StartLatitude = point.Latitude,
                                StartLongitude = point.Longitude,
                                EndTime = point.Timestamp
                            };
                        }
                    }
                }
            }

            // ✅ Finalize remaining drive or stop
            if (isMoving && currentDrivePoints.Count > 1)
            {
                var driveEvent = await CreateDriveEvent(currentDrivePoints, engineerId, dbContext);
                if (driveEvent != null)
                {
                    _logger.LogInformation($"🚗 FINAL DRIVE DETECTED: {currentDrivePoints.Count} pts");
                    events.Add(driveEvent);
                }
            }
            else if (lastStopEvent != null)
            {
                var stayDuration = (lastStopEvent.EndTime - lastStopEvent.StartTime).TotalMinutes;
                if (stayDuration >= MIN_STOP_DURATION_MIN)
                {
                    lastStopEvent.DurationMinutes = (int)stayDuration;

                    // ✅ Always save ongoing stop if duration threshold met
                    _logger.LogInformation($"🏠 ONGOING STOP DETECTED: {stayDuration:F1} min at ~{lastStopEvent.StartLatitude:F5},{lastStopEvent.StartLongitude:F5}");
                    events.Add(lastStopEvent);
                }
            }

            // 🚀 SMART MERGE consecutive drives that are close in time and space
            for (int i = 0; i < events.Count - 1; i++)
            {
                var current = events[i];
                var next = events[i + 1];

                if (current.Type == EventType.Drive && next.Type == EventType.Drive)
                {
                    double timeGap = (next.StartTime - current.EndTime).TotalMinutes;
                    double distanceGap = HaversineDistance(
                        new LocationPoint { Latitude = current.EndLatitude ?? 0, Longitude = current.EndLongitude ?? 0 },
                        new LocationPoint { Latitude = next.StartLatitude ?? 0, Longitude = next.StartLongitude ?? 0 }
                    ) * 1000;

                    if (timeGap < 5 && distanceGap < 200)
                    {
                        _logger.LogInformation($"🔁 MERGING consecutive drives: {timeGap:F1} min apart, {distanceGap:F1} m distance");

                        // Extend the current event to cover the next one
                        current.EndTime = next.EndTime;
                        current.EndLatitude = next.EndLatitude;
                        current.EndLongitude = next.EndLongitude;
                        current.DistanceKm = (current.DistanceKm ?? 0) + (next.DistanceKm ?? 0);
                        current.DurationMinutes += next.DurationMinutes;

                        // Merge route paths (if both have valid JSON arrays)
                        try
                        {
                            var coordsA = string.IsNullOrWhiteSpace(current.RoutePathJson)
                                ? new List<double[]>()
                                : JsonSerializer.Deserialize<List<double[]>>(current.RoutePathJson) ?? new();

                            var coordsB = string.IsNullOrWhiteSpace(next.RoutePathJson)
                                ? new List<double[]>()
                                : JsonSerializer.Deserialize<List<double[]>>(next.RoutePathJson) ?? new();

                            // Append B’s coordinates (excluding duplicates)
                            if (coordsB.Count > 0)
                            {
                                if (coordsA.Count > 0 &&
                                    coordsA.Last()[0] == coordsB.First()[0] &&
                                    coordsA.Last()[1] == coordsB.First()[1])
                                    coordsB.RemoveAt(0);

                                coordsA.AddRange(coordsB);
                                current.RoutePathJson = JsonSerializer.Serialize(coordsA);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"⚠️ Route merge failed: {ex.Message}");
                        }

                        // Remove the merged drive
                        events.RemoveAt(i + 1);
                        i--; // re-evaluate at same index
                    }
                }
            }



            _logger.LogInformation($"✅ Total events detected for Engineer {engineerId}: {events.Count}");
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
                StartTime = firstPoint.Timestamp.ToUniversalTime(),
                EndTime = lastPoint.Timestamp.ToUniversalTime(),
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

            // ✅ Compute total distance using all GPS points
            double totalDistance = 0;
            for (int i = 0; i < drivePoints.Count - 1; i++)
            {
                totalDistance += HaversineDistance(drivePoints[i], drivePoints[i + 1]);
            }

            // ✅ Optional: Smooth the route slightly (remove GPS jitter)
            // Only keep one point every ~15 meters to reduce data size
            var simplifiedPoints = DouglasPeucker.Simplify(drivePoints, 20); // 20 meters tolerance

            // ✅ Build coordinate list for visualization
            var coordinatePairs = simplifiedPoints.Select(p => new double[] { p.Longitude, p.Latitude }).ToList();
            string routePathJson = JsonSerializer.Serialize(coordinatePairs);

            // ✅ Reverse geocode start and end points
            var (_, startAddress) = await ReverseGeocodeAsync(firstPoint.Latitude, firstPoint.Longitude, dbContext);
            var (_, endAddress) = await ReverseGeocodeAsync(lastPoint.Latitude, lastPoint.Longitude, dbContext);

            // ✅ Construct the Drive ActivityEvent
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
                EndAddress = endAddress,

                // ✅ New field: JSON path of the actual GPS route
                RoutePathJson = routePathJson
            };
        }


        private async Task<(string LocationName, string Address)> ReverseGeocodeAsync(double lat, double lon, AppDbContext dbContext)
        {
            var httpClient = _httpClientFactory.CreateClient();
            // TODO: Move API Key to appsettings.json or other configuration provider
            // Using the API key from your old, working code.
            var apiKey = "pk.eyJ1IjoiYmFzaWwxLTIzIiwiYSI6ImNtZWFvNW43ZTA0ejQycHBtd3dkMHJ1bnkifQ.Y-IlM-vQAlaGr7pVQnug3Q";
            var url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lon},{lat}.json?types=poi,address&access_token={apiKey}";

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
                            // Using the logic from your old code to get location name and address
                            var locationName = feature.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "Unknown" : "Unknown";
                            var address = feature.TryGetProperty("place_name", out var placeNameProp) ? placeNameProp.GetString() ?? "Unknown Address" : "Unknown Address";

                            return (locationName, address);
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

        //Douglas Peucker algorith
        public static class DouglasPeucker
        {
            public static List<LocationPoint> Simplify(List<LocationPoint> points, double toleranceMeters)
            {
                if (points.Count < 3) return points;

                double toleranceDegrees = toleranceMeters / 111_320.0; // rough conversion
                int firstIndex = 0;
                int lastIndex = points.Count - 1;
                var keep = new bool[points.Count];
                keep[firstIndex] = true;
                keep[lastIndex] = true;

                SimplifySection(points, firstIndex, lastIndex, toleranceDegrees, keep);

                var simplified = new List<LocationPoint>();
                for (int i = 0; i < points.Count; i++)
                {
                    if (keep[i]) simplified.Add(points[i]);
                }
                return simplified;
            }

            private static void SimplifySection(List<LocationPoint> pts, int first, int last, double tolerance, bool[] keep)
            {
                if (last <= first + 1) return;

                double maxDist = 0;
                int index = 0;

                var start = pts[first];
                var end = pts[last];

                for (int i = first + 1; i < last; i++)
                {
                    double dist = PerpendicularDistance(pts[i], start, end);
                    if (dist > maxDist)
                    {
                        maxDist = dist;
                        index = i;
                    }
                }

                if (maxDist > tolerance)
                {
                    keep[index] = true;
                    SimplifySection(pts, first, index, tolerance, keep);
                    SimplifySection(pts, index, last, tolerance, keep);
                }
            }

            private static double PerpendicularDistance(LocationPoint p, LocationPoint start, LocationPoint end)
            {
                double dx = end.Longitude - start.Longitude;
                double dy = end.Latitude - start.Latitude;

                if (dx == 0 && dy == 0)
                    return Math.Sqrt(Math.Pow(p.Longitude - start.Longitude, 2) + Math.Pow(p.Latitude - start.Latitude, 2));

                double t = ((p.Longitude - start.Longitude) * dx + (p.Latitude - start.Latitude) * dy) / (dx * dx + dy * dy);
                if (t < 0)
                    return Math.Sqrt(Math.Pow(p.Longitude - start.Longitude, 2) + Math.Pow(p.Latitude - start.Latitude, 2));
                else if (t > 1)
                    return Math.Sqrt(Math.Pow(p.Longitude - end.Longitude, 2) + Math.Pow(p.Latitude - end.Latitude, 2));

                double projX = start.Longitude + t * dx;
                double projY = start.Latitude + t * dy;
                return Math.Sqrt(Math.Pow(p.Longitude - projX, 2) + Math.Pow(p.Latitude - projY, 2));
            }
        }



        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}