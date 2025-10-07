using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EcsFeMappingApi.Services
{
    public class TripDetectionService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<TripDetectionService> _logger;
        private readonly HttpClient _httpClient;

        // --- Modified Constants ---
        private const double SpeedThresholdMph = 2.0; // Lowered - more sensitive to movement
        private const int StationaryTimeMinutes = 10; // 10 minutes instead of 15
        private const double MinTripDistanceMiles = 0.1; // Lowered - even short moves count
        private const double StationaryRadiusMeters = 50; // 50 meter radius = "same location"
        private const string MAPBOX_API_KEY = "pk.eyJ1IjoiYmFzaWwxLTIzIiwiYSI6ImNtZWFvNW43ZTA0ejQycHBtd3dkMHJ1bnkifQ.Y-IlM-vQAlaGr7pVQnug3Q"; 

        public TripDetectionService(AppDbContext context, ILogger<TripDetectionService> logger, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
        }

        public async Task ProcessLocationPoints(IEnumerable<LocationPoint> points)
        {
            if (!points.Any()) return;

            foreach (var point in points.OrderBy(p => p.Timestamp))
            {
                await ProcessSingleLocationPoint(point);
            }
        }

        private async Task ProcessSingleLocationPoint(LocationPoint currentPoint)
        {
            try
            {
                // Get reverse geocoding first
                var address = await GetAddressFromCoordinates(currentPoint.Latitude, currentPoint.Longitude);
                currentPoint.Address = address;

                // Get the last location point for this field engineer
                var lastPoint = await _context.LocationPoints
                    .Where(lp => lp.FieldEngineerId == currentPoint.FieldEngineerId)
                    .OrderByDescending(lp => lp.Timestamp)
                    .FirstOrDefaultAsync();

                // Check for ongoing trip
                var ongoingTrip = await _context.Trips
                    .Where(t => t.FieldEngineerId == currentPoint.FieldEngineerId && t.EndTime == null)
                    .FirstOrDefaultAsync();

                if (lastPoint != null)
                {
                    // Calculate distance and time difference
                    var distanceMeters = CalculateDistanceInMeters(
                        lastPoint.Latitude, lastPoint.Longitude,
                        currentPoint.Latitude, currentPoint.Longitude);
                    
                    var timeDifference = currentPoint.Timestamp - lastPoint.Timestamp;
                    var isStationary = distanceMeters <= StationaryRadiusMeters;

                    _logger.LogInformation($"FE {currentPoint.FieldEngineerId}: Distance={distanceMeters:F1}m, TimeDiff={timeDifference.TotalMinutes:F1}min, Stationary={isStationary}");

                    if (isStationary && timeDifference.TotalMinutes >= StationaryTimeMinutes)
                    {
                        // FE has been stationary for 10+ minutes - create/update trip entry
                        if (ongoingTrip == null)
                        {
                            // Create new stationary trip
                            var newTrip = new TripModel
                            {
                                FieldEngineerId = currentPoint.FieldEngineerId,
                                StartTime = lastPoint.Timestamp,
                                StartLatitude = lastPoint.Latitude,
                                StartLongitude = lastPoint.Longitude,
                                StartLocation = lastPoint.Address ?? address,
                                StartAddress = lastPoint.Address ?? address, // Map to original field
                                EndLatitude = currentPoint.Latitude,
                                EndLongitude = currentPoint.Longitude,
                                EndLocation = address,
                                EndAddress = address, // Map to original field
                                TripType = "STATIONARY",
                                TotalDistance = 0,
                                Distance = 0, // Map to original field
                            };

                            _context.Trips.Add(newTrip);
                            _logger.LogInformation($"✅ Created STATIONARY trip for FE {currentPoint.FieldEngineerId} at {address}");
                        }
                        else
                        {
                            // Update ongoing trip
                            ongoingTrip.EndLatitude = currentPoint.Latitude;
                            ongoingTrip.EndLongitude = currentPoint.Longitude;
                            ongoingTrip.EndLocation = address;
                            ongoingTrip.EndAddress = address; // Update original field too
                        }
                    }
                    else if (!isStationary && distanceMeters > 100) // Significant movement
                    {
                        // FE is moving - end any stationary trip and start movement trip
                        if (ongoingTrip != null && ongoingTrip.TripType == "STATIONARY")
                        {
                            // End the stationary trip
                            ongoingTrip.EndTime = currentPoint.Timestamp;
                            ongoingTrip.EndLatitude = currentPoint.Latitude;
                            ongoingTrip.EndLongitude = currentPoint.Longitude;
                            ongoingTrip.EndLocation = address;
                            
                            _logger.LogInformation($"🔚 Ended STATIONARY trip for FE {currentPoint.FieldEngineerId}");
                        }

                        // Create new movement trip
                        var movementTrip = new TripModel
                        {
                            FieldEngineerId = currentPoint.FieldEngineerId,
                            StartTime = lastPoint.Timestamp,
                            StartLatitude = lastPoint.Latitude,
                            StartLongitude = lastPoint.Longitude,
                            StartLocation = lastPoint.Address ?? "Unknown",
                            EndLatitude = currentPoint.Latitude,
                            EndLongitude = currentPoint.Longitude,
                            EndAddress = address, // Map to original field
                            EndLocation = address,
                            TripType = "MOVEMENT",
                            TotalDistance = distanceMeters / 1000.0, // Convert to km
                            // EndTime = null, // Ongoing until they stop
                        };

                        _context.Trips.Add(movementTrip);
                        _logger.LogInformation($"🚗 Created MOVEMENT trip for FE {currentPoint.FieldEngineerId}");
                    }
                }

                // Save the location point
                _context.LocationPoints.Add(currentPoint);
                await _context.SaveChangesAsync();

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing location point for FE {currentPoint.FieldEngineerId}");
            }
        }

        private async Task<string> GetAddressFromCoordinates(double latitude, double longitude)
        {
            try
            {
                var url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{longitude},{latitude}.json?access_token={MAPBOX_API_KEY}";
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var jsonDoc = JsonDocument.Parse(content);
                    
                    if (jsonDoc.RootElement.TryGetProperty("features", out var features) && features.GetArrayLength() > 0)
                    {
                        var placeName = features[0].GetProperty("place_name").GetString();
                        return placeName ?? $"{latitude:F6}, {longitude:F6}";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting address from Mapbox");
            }

            return $"{latitude:F6}, {longitude:F6}";
        }

        private double CalculateDistanceInMeters(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371000; // Earth's radius in meters
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private double ToRadians(double angle) => (Math.PI / 180) * angle;
    }
}