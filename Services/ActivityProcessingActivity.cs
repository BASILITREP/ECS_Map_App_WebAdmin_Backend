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

                    // ✅ Mark processed points (leave last few minutes unprocessed for continuity)
                    var nowUtc = DateTime.UtcNow;
                    var keepWindowMinutes = 1; // keep last 3 mins open
                    int marked = 0, kept = 0;

                    foreach (var p in pointsToProcess)
                    {
                        if (p.Timestamp <= nowUtc.AddMinutes(-keepWindowMinutes))
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
                    _logger.LogInformation($"✅ Finished FE #{engineerId}: marked {marked} points as processed, kept {kept} recent ones.");


                    // ✅ Merge consecutive drives to reduce clutter
                    await MergeConsecutiveDriveEvents(engineerId, dbContext);
                    // ✅ Merge consecutive stays to reduce clutter
                    await MergeConsecutiveStayEvents(engineerId, dbContext);

                    // ✅ Optional: Cleanup old processed points to save space
                    // var cleanupThreshold = DateTime.UtcNow.AddDays(-2);
                    // var oldPoints = dbContext.LocationPoints
                    // .Where(p => p.IsProcessed && p.Timestamp < cleanupThreshold);
                    // int deletedCount = await oldPoints.ExecuteDeleteAsync();
                    // _logger.LogInformation($"🧹 Cleaned up {deletedCount} old processed points for FE #{engineerId}.");


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
            var events = new List<ActivityEvent>();
            if (locationPoints.Count == 0) return events;

            // === Test indoors ===
            // const double STAY_RADIUS_METERS = 5;       // ~12m tolerance for jitter
            // const double MOVE_SPEED_THRESHOLD_KMH = 0.5;//1.0
            // const double STOP_SPEED_THRESHOLD_KMH = 3.0;
            // const int DRIVE_STOP_THRESHOLD_MIN = 1;     // stop ≥2 min to end drive
            // const int STAY_MIN_DURATION_MIN = 1;        // min stay = 1 min
            // const int MIN_TRIP_POINTS = 2;
            //         const double MIN_TRIP_DISTANCE_KM = 0.01;   // 0.02

            // === FIELD MODE (REAL-WORLD USE) ===
            const double STAY_RADIUS_METERS = 12;       // consider as same place if within 12 meters
            const double MOVE_SPEED_THRESHOLD_KMH = 1.0; // minimum speed to start moving
            const double STOP_SPEED_THRESHOLD_KMH = 3.0; // threshold for slow/stationary
            const int DRIVE_STOP_THRESHOLD_MIN = 2;      // must stop ≥2 mins to end a drive
            const int STAY_MIN_DURATION_MIN = 1;         // must stay ≥1 min to count as stop
            const int MIN_TRIP_POINTS = 2;               // at least 2 valid points per drive
            const double MIN_TRIP_DISTANCE_KM = 0.02;    // minimum trip distance ≈ 20 meters


            bool isDriving = false;
            DateTime? lastStopTime = null;
            var drivePoints = new List<LocationPoint>();
            ActivityEvent? currentStay = null;

            _logger.LogInformation($"🧭 DetectEvents FE #{engineerId} ({locationPoints.Count} pts)");

            for (int i = 0; i < locationPoints.Count; i++)
            {
                var point = locationPoints[i];

                // --- compute speed ---
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
                    else speedKmh = 0;
                }
                else speedKmh = (point.Speed ?? 0) * 3.6;

                // ===========================================================
                // ===============  MOVEMENT / DRIVE DETECTION  ===============
                // ===========================================================
                if (speedKmh > MOVE_SPEED_THRESHOLD_KMH)
                {
                    // user is moving
                    if (!isDriving)
                    {
                        _logger.LogInformation($"🚗 Drive started at {point.Timestamp:HH:mm:ss}");
                        isDriving = true;
                        drivePoints.Clear();
                        drivePoints.Add(point);

                        // End ongoing stay if any
                        if (currentStay != null)
                        {
                            var stayDuration = (point.Timestamp - currentStay.StartTime).TotalMinutes;
                            if (stayDuration >= STAY_MIN_DURATION_MIN)
                            {
                                currentStay.EndTime = point.Timestamp;
                                currentStay.DurationMinutes = (int)stayDuration;
                                var (locName, addr) = await ReverseGeocodeAsync(
                                    currentStay.StartLatitude ?? 0,
                                    currentStay.StartLongitude ?? 0,
                                    dbContext
                                );
                                currentStay.LocationName = locName;
                                currentStay.Address = addr;
                                events.Add(currentStay);
                                _logger.LogInformation($"🏠 Stay ended: {addr}");
                            }
                            currentStay = null;
                        }
                    }
                    else drivePoints.Add(point);

                    lastStopTime = null;
                }
                else
                {
                    // user is slow or stationary
                    if (isDriving)
                    {
                        // maybe ending a drive
                        if (lastStopTime == null)
                            lastStopTime = point.Timestamp;
                        else
                        {
                            var stoppedFor = (point.Timestamp - lastStopTime.Value).TotalMinutes;
                            if (stoppedFor >= DRIVE_STOP_THRESHOLD_MIN && drivePoints.Count >= MIN_TRIP_POINTS)
                            {
                                // drive ends
                                var driveEvent = await CreateDriveEvent(drivePoints, engineerId, dbContext);

                                if (driveEvent != null && driveEvent.DistanceKm >= MIN_TRIP_DISTANCE_KM)
                                {
                                    // avoid duplicate or near-identical drives
                                    bool isDuplicate = events.Any(e =>
                                        e.Type == EventType.Drive &&
                                        Math.Abs((e.EndTime - driveEvent.EndTime).TotalMinutes) < 2 &&
                                        HaversineDistance(
                                            new LocationPoint { Latitude = e.EndLatitude ?? 0, Longitude = e.EndLongitude ?? 0 },
                                            new LocationPoint { Latitude = driveEvent.EndLatitude ?? 0, Longitude = driveEvent.EndLongitude ?? 0 }
                                        ) * 1000 < 100
                                    );

                                    if (!isDuplicate)
                                    {
                                        events.Add(driveEvent);
                                        _logger.LogInformation($"🏁 Drive completed: {driveEvent.DistanceKm:F2} km, {driveEvent.DurationMinutes} min");
                                    }
                                    else
                                    {
                                        _logger.LogInformation($"⚠️ Skipped duplicate drive ({driveEvent.DistanceKm:F2} km)");
                                    }
                                }

                                drivePoints.Clear();
                                isDriving = false;

                                // start new stay
                                currentStay = new ActivityEvent
                                {
                                    FieldEngineerId = engineerId,
                                    Type = EventType.Stop,
                                    StartTime = point.Timestamp,
                                    StartLatitude = point.Latitude,
                                    StartLongitude = point.Longitude
                                };
                                _logger.LogInformation($"🅿️ Stay started at {point.Timestamp:HH:mm:ss}");
                            }
                        }
                    }
                    else
                    {
                        // staying still
                        if (currentStay == null)
                        {
                            currentStay = new ActivityEvent
                            {
                                FieldEngineerId = engineerId,
                                Type = EventType.Stop,
                                StartTime = point.Timestamp,
                                StartLatitude = point.Latitude,
                                StartLongitude = point.Longitude
                            };
                        }
                        else
                        {
                            // extend stay duration
                            var distFromStay = HaversineDistance(
                                new LocationPoint { Latitude = point.Latitude, Longitude = point.Longitude },
                                new LocationPoint { Latitude = currentStay.StartLatitude ?? 0, Longitude = currentStay.StartLongitude ?? 0 }
                            ) * 1000;

                            if (distFromStay <= STAY_RADIUS_METERS)
                                currentStay.EndTime = point.Timestamp;
                            else
                            {
                                var stayDuration = (currentStay.EndTime - currentStay.StartTime).TotalMinutes;
                                if (stayDuration >= STAY_MIN_DURATION_MIN)
                                {
                                    currentStay.DurationMinutes = (int)stayDuration;
                                    var (locName, addr) = await ReverseGeocodeAsync(
                                        currentStay.StartLatitude ?? 0,
                                        currentStay.StartLongitude ?? 0,
                                        dbContext
                                    );
                                    currentStay.LocationName = locName;
                                    currentStay.Address = addr;
                                    events.Add(currentStay);
                                    _logger.LogInformation($"🏠 Stay confirmed: {addr}");
                                }

                                currentStay = new ActivityEvent
                                {
                                    FieldEngineerId = engineerId,
                                    Type = EventType.Stop,
                                    StartTime = point.Timestamp,
                                    StartLatitude = point.Latitude,
                                    StartLongitude = point.Longitude
                                };
                            }
                        }
                    }
                }
            }

            // finalize any remaining
            if (isDriving && drivePoints.Count >= MIN_TRIP_POINTS)
            {
                var driveEvent = await CreateDriveEvent(drivePoints, engineerId, dbContext);
                if (driveEvent != null && driveEvent.DistanceKm >= MIN_TRIP_DISTANCE_KM)
                {
                    events.Add(driveEvent);
                    _logger.LogInformation($"🚗 Final drive saved ({driveEvent.DistanceKm:F2} km)");
                }
            }

            if (currentStay != null)
            {
                var stayDuration = (locationPoints.Last().Timestamp - currentStay.StartTime).TotalMinutes;
                if (stayDuration >= STAY_MIN_DURATION_MIN)
                {
                    currentStay.EndTime = locationPoints.Last().Timestamp;
                    currentStay.DurationMinutes = (int)stayDuration;
                    var (locName, addr) = await ReverseGeocodeAsync(
                        currentStay.StartLatitude ?? 0,
                        currentStay.StartLongitude ?? 0,
                        dbContext
                    );
                    currentStay.LocationName = locName;
                    currentStay.Address = addr;
                    events.Add(currentStay);
                    _logger.LogInformation($"🏠 Final stay recorded: {addr}");
                }
            }

            // 🔁 merge duplicates
            events = events
                .GroupBy(e => new
                {
                    e.Type,
                    StartLat = Math.Round(e.StartLatitude ?? 0, 5),
                    StartLon = Math.Round(e.StartLongitude ?? 0, 5),
                    EndLat = Math.Round(e.EndLatitude ?? 0, 5),
                    EndLon = Math.Round(e.EndLongitude ?? 0, 5)
                })
                .Select(g => g.First())
                .ToList();

            _logger.LogInformation($"✅ Final total events for FE #{engineerId}: {events.Count}");
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

            // 🚫 Skip extremely short or stationary drives (avoid duplicates / fake drives)
            var totalDuration = (lastPoint.Timestamp - firstPoint.Timestamp).TotalMinutes;
            if (totalDistance < 0.02 || totalDuration < 1)
            {
                _logger.LogInformation(
                    $"⏸ Ignored micro drive for FE #{engineerId} ({totalDistance:F3} km, {totalDuration:F1} min)");
                return null;
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


            //Compute Fare
            const double BASE_FARE = 25.0;
            const double BASE_DISTANCE_KM = 2.5;
            const double EXTRA_DISTANCE_STEP_KM = 1.5;
            const double EXTRA_FARE_AMOUNT = 5.0;

            double fare = BASE_FARE;

            if (totalDistance > BASE_DISTANCE_KM)
            {
                var extraDistance = totalDistance - BASE_DISTANCE_KM;
                var steps = Math.Ceiling(extraDistance / EXTRA_DISTANCE_STEP_KM);
                fare += steps * EXTRA_FARE_AMOUNT;
            }

            //Compute Travel Time
            TimeSpan travelTimeSpan = lastPoint.Timestamp - firstPoint.Timestamp;
            string travelTime = $"{(int)travelTimeSpan.TotalHours:D2}:{travelTimeSpan.Minutes:D2}";



            // ✅ Construct the Drive ActivityEvent
            return new ActivityEvent
            {
                FieldEngineerId = engineerId,
                Type = EventType.Drive,
                StartTime = firstPoint.Timestamp,
                EndTime = lastPoint.Timestamp,
                DurationMinutes = (int)totalDuration,
                DistanceKm = totalDistance,
                TopSpeedKmh = drivePoints.Max(p => p.Speed ?? 0) * 3.6,
                TravelTimeCategory = travelTime,
                CalculatedFare = fare,
                StartLatitude = firstPoint.Latitude,
                StartLongitude = firstPoint.Longitude,
                EndLatitude = lastPoint.Latitude,
                EndLongitude = lastPoint.Longitude,
                StartAddress = startAddress,
                EndAddress = endAddress,
                RoutePathJson = routePathJson
            };
        }

        //Merge drive events that are very close in time and location
        private async Task MergeConsecutiveDriveEvents(int engineerId, AppDbContext dbContext)
        {
            var drives = await dbContext.ActivityEvents
                .Where(e => e.FieldEngineerId == engineerId && e.Type == EventType.Drive)
                .OrderBy(e => e.StartTime)
                .ToListAsync();

            if (drives.Count < 2) return;

            for (int i = 0; i < drives.Count - 1; i++)
            {
                var current = drives[i];
                var next = drives[i + 1];

                double gapMinutes = (next.StartTime - current.EndTime).TotalMinutes;
                double gapDistance = HaversineDistance(
                    new LocationPoint { Latitude = current.EndLatitude ?? 0, Longitude = current.EndLongitude ?? 0 },
                    new LocationPoint { Latitude = next.StartLatitude ?? 0, Longitude = next.StartLongitude ?? 0 }
                ) * 1000; // meters

                if (gapMinutes < 10 && gapDistance < 100)
                {
                    _logger.LogInformation($"🔁 Merging consecutive drives (Gap={gapMinutes:F1}min, {gapDistance:F1}m)");

                    // Merge key fields
                    current.EndTime = next.EndTime;
                    current.EndLatitude = next.EndLatitude;
                    current.EndLongitude = next.EndLongitude;
                    current.DurationMinutes += next.DurationMinutes;
                    current.DistanceKm = (current.DistanceKm ?? 0) + (next.DistanceKm ?? 0);
                    current.TopSpeedKmh = Math.Max(current.TopSpeedKmh ?? 0, next.TopSpeedKmh ?? 0);

                    // Combine RoutePathJson if both exist
                    try
                    {
                        var coordsA = string.IsNullOrWhiteSpace(current.RoutePathJson)
                            ? new List<double[]>()
                            : JsonSerializer.Deserialize<List<double[]>>(current.RoutePathJson) ?? new();

                        var coordsB = string.IsNullOrWhiteSpace(next.RoutePathJson)
                            ? new List<double[]>()
                            : JsonSerializer.Deserialize<List<double[]>>(next.RoutePathJson) ?? new();

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

                    // Remove the next (merged) event
                    dbContext.ActivityEvents.Remove(next);

                    // Move the pointer back one to re-check newly merged block
                    drives.RemoveAt(i + 1);
                    i--;
                }
            }

            await dbContext.SaveChangesAsync();
            _logger.LogInformation($"✅ Completed drive merge for FE #{engineerId}");
        }

        //Merge stay events that are very close in time and location
        private async Task MergeConsecutiveStayEvents(int engineerId, AppDbContext dbContext)
            {
                var stays = await dbContext.ActivityEvents
                    .Where(e => e.FieldEngineerId == engineerId && e.Type == EventType.Stop)
                    .OrderBy(e => e.StartTime)
                    .ToListAsync();

                if (stays.Count < 2) return;

                for (int i = 0; i < stays.Count - 1; i++)
                {
                    var current = stays[i];
                    var next = stays[i + 1];

                    // ✅ Compute gap and distance
                    double gapMinutes = (next.StartTime - current.EndTime).TotalMinutes;
                    double gapDistance = HaversineDistance(
                        new LocationPoint { Latitude = current.EndLatitude ?? current.StartLatitude ?? 0, Longitude = current.EndLongitude ?? current.StartLongitude ?? 0 },
                        new LocationPoint { Latitude = next.StartLatitude ?? 0, Longitude = next.StartLongitude ?? 0 }
                    ) * 1000; // meters

                    // ✅ Merge condition: within 5 minutes gap, within 50 meters radius
                    if (gapMinutes <= 5 && gapDistance <= 50)
                    {
                        _logger.LogInformation($"🔁 Merging consecutive stays (Gap={gapMinutes:F1}min, Dist={gapDistance:F1}m)");

                        // Extend the current stay’s end time and duration
                        current.EndTime = next.EndTime;
                        current.DurationMinutes += next.DurationMinutes;

                        // Preserve location info if current is missing
                        if (string.IsNullOrWhiteSpace(current.LocationName) && !string.IsNullOrWhiteSpace(next.LocationName))
                            current.LocationName = next.LocationName;
                        if (string.IsNullOrWhiteSpace(current.Address) && !string.IsNullOrWhiteSpace(next.Address))
                            current.Address = next.Address;

                        // Delete the merged record
                        dbContext.ActivityEvents.Remove(next);

                        // Move pointer back one step to recheck merged block
                        stays.RemoveAt(i + 1);
                        i--;
                    }
                }

                await dbContext.SaveChangesAsync();
                _logger.LogInformation($"✅ Completed stay merge for FE #{engineerId}");
            }





        private async Task<(string LocationName, string Address)> ReverseGeocodeAsync(double lat, double lon, AppDbContext dbContext)
        {
            if (lat == 0 || lon == 0)
                return ("Unknown", "Unknown Address");

            var httpClient = _httpClientFactory.CreateClient();
            var apiKey = "pk.eyJ1IjoiYmFzaWwxLTIzIiwiYSI6ImNtZWFvNW43ZTA0ejQycHBtd3dkMHJ1bnkifQ.Y-IlM-vQAlaGr7pVQnug3Q";
            var url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lon},{lat}.json?types=address,neighborhood,locality&access_token={apiKey}";

            //var url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lon},{lat}.json?types=poi,address&access_token={apiKey}";

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

                // ✅ Choose most relevant address-type feature
        JsonElement? selectedFeature = null;
        foreach (var f in features.EnumerateArray())
        {
            if (f.TryGetProperty("place_type", out var types))
            {
                var type = types[0].GetString();
                if (type == "address" || type == "neighborhood" || type == "locality")
                {
                    selectedFeature = f;
                    break;
                }
            }
        }

        var feature = selectedFeature ?? features[0];
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