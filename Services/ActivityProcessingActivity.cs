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
using Microsoft.AspNetCore.SignalR;

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
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        }

        private void DoWork(object? state)
        {
            _logger.LogInformation("🕒 DoWork triggered - background service is alive.");
            _logger.LogInformation("Activity Processing Service is running via timer.");
            Task.Run(async () => await ProcessActivities());
        }

        public async Task TriggerProcessingAsync()
        {
            _logger.LogInformation("Activity Processing Service is being triggered manually.");
            await ProcessActivities();
        }

        private static string ComputeEventKey(ActivityEvent ev)
        {
            // Round lat/lon to 5 decimals to group small GPS jitter together
            string startLat = Math.Round(ev.StartLatitude ?? ev.Latitude ?? 0, 5).ToString();
            string startLon = Math.Round(ev.StartLongitude ?? ev.Longitude ?? 0, 5).ToString();
            string endLat = Math.Round(ev.EndLatitude ?? ev.Latitude ?? 0, 5).ToString();
            string endLon = Math.Round(ev.EndLongitude ?? ev.Longitude ?? 0, 5).ToString();
            string keyBase = $"{ev.FieldEngineerId}|{ev.Type}|{startLat},{startLon}|{endLat},{endLon}|{ev.StartTime:yyyyMMddHHmm}";
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(keyBase);
                var hashBytes = sha1.ComputeHash(bytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 12);
            }
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
    _logger.LogInformation($"⚙️ Processing activities for Engineer ID: {engineerId}");

    var pointsToProcess = await dbContext.LocationPoints
        .Where(p => p.FieldEngineerId == engineerId && !p.IsProcessed)
        .OrderBy(p => p.Timestamp)
        .ToListAsync();

    // ✅ Skip if not enough data points for analysis
    if (pointsToProcess.Count < 3)
    {
        _logger.LogInformation($"⏸️ Skipping FE #{engineerId}: only {pointsToProcess.Count} unprocessed points (need ≥3).");
        continue;
    }

    _logger.LogInformation($"🔍 Found {pointsToProcess.Count} unprocessed points for FE #{engineerId}.");

    var newEvents = await DetectEvents(pointsToProcess, engineerId, dbContext);

    if (newEvents.Any())
    {
        await dbContext.ActivityEvents.AddRangeAsync(newEvents);
        _logger.LogInformation($"🆕 Added {newEvents.Count} new events for FE #{engineerId}.");
    }
    else
    {
        _logger.LogInformation($"📭 No new events detected for FE #{engineerId} (points analyzed: {pointsToProcess.Count}).");
    }

    // ✅ Leave a trailing window unprocessed so stops/drives can accumulate
    var nowUtc = DateTime.UtcNow;
    var windowMinutes = 5; // Keep last 5 minutes unprocessed for continuity
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
            kept++;
        }
    }

    await dbContext.SaveChangesAsync();
    _logger.LogInformation($"✅ Finished FE #{engineerId}: marked {marked}, kept {kept} recent points for next cycle.");

    // ✅ Broadcast updated coordinates to web admin
    try
    {
        var engineer = await dbContext.FieldEngineers.FindAsync(engineerId);
        if (engineer != null)
        {
            using (var innerScope = _serviceProvider.CreateScope())
            {
                var hubContext = innerScope.ServiceProvider
                    .GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<EcsFeMappingApi.Services.NotificationHub>>();

                await hubContext.Clients.All.SendAsync("ReceiveFieldEngineerUpdate", engineer);
                _logger.LogInformation($"📡 Broadcasted live update for FE #{engineer.Id}: {engineer.CurrentLatitude},{engineer.CurrentLongitude}");
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError($"❌ SignalR broadcast failed: {ex.Message}");
    }
}

            }
        }

        private async Task<List<ActivityEvent>> DetectEvents(List<LocationPoint> locationPoints, int engineerId, AppDbContext dbContext)
{
    _logger.LogInformation($"🧩 DetectEvents() called for FE #{engineerId} with {locationPoints.Count} points.");

    var events = new List<ActivityEvent>();
    if (locationPoints.Count < 2)
    {
        _logger.LogInformation($"🚫 Not enough points for FE #{engineerId} (only {locationPoints.Count}).");
        return events;
    }

    const double MOVE_DISTANCE_METERS = 2;   // Lowered for test
    const int STOP_DURATION_MINUTES = 1;     // Drive trigger (short for test)
    const int STAY_DURATION_MINUTES = 2;     // Stay trigger (short for test)
    const double STAY_RADIUS_METERS = 100;   // Looser radius for test

    LocationPoint pointA = locationPoints.First(); 
    DateTime? stopStartTime = null;
    LocationPoint? stopStartPoint = null;
    bool isStopped = false;

    _logger.LogInformation($"🚀 Simple detection for FE #{engineerId} started ({locationPoints.Count} pts)");

    for (int i = 1; i < locationPoints.Count; i++)
    {
        var prev = locationPoints[i - 1];
        var curr = locationPoints[i];

        double distMeters = HaversineDistance(prev, curr) * 1000.0;
        double speedKmh = (curr.Speed ?? 0) * 3.6;

        _logger.LogInformation($"[{engineerId}] {curr.Timestamp:HH:mm:ss} | {distMeters:F1} m | {speedKmh:F1} km/h");

        // Movement detection
        if (distMeters > MOVE_DISTANCE_METERS)
        {
            // Reset stop timers if moving
            stopStartTime = null;
            stopStartPoint = null;
            isStopped = false;
        }
        else
        {
            // Within stay radius → possible stop
            stopStartTime ??= prev.Timestamp;
            stopStartPoint ??= prev;

            var stopDuration = (curr.Timestamp - stopStartTime.Value).TotalMinutes;

            // 🧱 Debug how long we've been stopped
            _logger.LogInformation($"[{engineerId}] Stop duration so far: {stopDuration:F1} min");

            // --- DRIVE EVENT (after STOP_DURATION_MINUTES)
            if (stopDuration >= STOP_DURATION_MINUTES && !isStopped)
            {
                var driveEvent = new ActivityEvent
                {
                    FieldEngineerId = engineerId,
                    Type = EventType.Drive,
                    StartTime = pointA.Timestamp,
                    EndTime = curr.Timestamp,
                    StartLatitude = pointA.Latitude,
                    StartLongitude = pointA.Longitude,
                    EndLatitude = curr.Latitude,
                    EndLongitude = curr.Longitude,
                    DistanceKm = HaversineDistance(pointA, curr),
                    DurationMinutes = (int)stopDuration,
                };

                events.Add(driveEvent);
                _logger.LogInformation($"🚗 DRIVE EVENT: {driveEvent.DistanceKm:F2} km from Point A→B");
                pointA = curr;
            }

            // --- STAY EVENT (after STAY_DURATION_MINUTES)
            if (stopDuration >= STAY_DURATION_MINUTES)
            {
                var stayEvent = new ActivityEvent
                {
                    FieldEngineerId = engineerId,
                    Type = EventType.Stop,
                    StartTime = stopStartTime.Value,
                    EndTime = curr.Timestamp,
                    Latitude = stopStartPoint!.Latitude,
                    Longitude = stopStartPoint!.Longitude,
                    DurationMinutes = (int)stopDuration,
                };

                // Prevent duplicates in same radius
                bool alreadyLogged = events.Any(e =>
                    e.Type == EventType.Stop &&
                    HaversineDistance(
                        new LocationPoint { Latitude = e.Latitude ?? 0, Longitude = e.Longitude ?? 0 },
                        new LocationPoint { Latitude = stayEvent.Latitude ?? 0, Longitude = stayEvent.Longitude ?? 0 }
                    ) * 1000 < STAY_RADIUS_METERS
                );

                if (!alreadyLogged)
                {
                    try
                    {
                        // 🌍 Reverse geocode
                        var (locationName, address) = await ReverseGeocodeAsync(
                            stayEvent.Latitude ?? 0,
                            stayEvent.Longitude ?? 0,
                            dbContext
                        );

                        stayEvent.LocationName = locationName;
                        stayEvent.Address = address;

                        await dbContext.ActivityEvents.AddAsync(stayEvent);
                        await dbContext.SaveChangesAsync();

                        events.Add(stayEvent);
                        _logger.LogInformation($"🏠 STAY EVENT: {stopDuration:F1} min at {address} ({stayEvent.Latitude:F5},{stayEvent.Longitude:F5})");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"🔥 Error creating stay event for FE #{engineerId}");
                    }
                }

                isStopped = true;
            }
        }
    }

    _logger.LogInformation($"✅ Detected {events.Count} events for FE #{engineerId}");
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
            if (lat == 0 || lon == 0)
                return ("Unknown", "Unknown Address");

            var httpClient = _httpClientFactory.CreateClient();
            var apiKey = "pk.eyJ1IjoiYmFzaWwxLTIzIiwiYSI6ImNtZWFvNW43ZTA0ejQycHBtd3dkMHJ1bnkifQ.Y-IlM-vQAlaGr7pVQnug3Q";
            var url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lon},{lat}.json?types=poi,address&access_token={apiKey}";

            try
            {
                var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Reverse geocoding failed with status {response.StatusCode} for {lat},{lon}");
                    return ("Unknown", "Unknown Address");
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(jsonString);

                if (!jsonDoc.RootElement.TryGetProperty("features", out var features) || features.GetArrayLength() == 0)
                    return ("Unknown", "Unknown Address");

                var feature = features[0];
                var locationName = feature.TryGetProperty("text", out var textProp)
                    ? textProp.GetString() ?? "Unknown"
                    : "Unknown";
                var address = feature.TryGetProperty("place_name", out var placeNameProp)
                    ? placeNameProp.GetString() ?? "Unknown Address"
                    : "Unknown Address";

                return (locationName, address);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"🔥 Reverse geocoding crashed for {lat},{lon}");
                return ("Unknown", "Unknown Address");
            }
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