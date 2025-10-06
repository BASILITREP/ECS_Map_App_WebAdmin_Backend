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

        // --- Constants ---
        private const double SpeedThresholdMph = 5.0; 
        private const int StationaryTimeMinutes = 15;
        private const double MinTripDistanceMiles = 0.5;
        // Kinuha ko mula sa luma mong service
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

            var fieldEngineerId = points.First().FieldEngineerId;
            var orderedPoints = points.OrderBy(p => p.Timestamp).ToList();

            foreach (var point in orderedPoints)
            {
                var lastPoint = await _context.LocationPoints
                    .Where(p => p.FieldEngineerId == fieldEngineerId)
                    .OrderByDescending(p => p.Timestamp)
                    .FirstOrDefaultAsync();

                if (lastPoint == null)
                {
                    _context.LocationPoints.Add(point);
                    await _context.SaveChangesAsync();
                    continue;
                }

                var timeDifference = point.Timestamp - lastPoint.Timestamp;
                var distance = CalculateDistance(lastPoint.Latitude, lastPoint.Longitude, point.Latitude, point.Longitude);
                var speed = timeDifference.TotalHours > 0 ? distance / timeDifference.TotalHours : 0;

                var ongoingTrip = await _context.Trips
                    .Include(t => t.Path)
                    .Where(t => t.FieldEngineerId == fieldEngineerId && t.EndTime == null)
                    .FirstOrDefaultAsync();

                if (speed >= SpeedThresholdMph)
                {
                    if (ongoingTrip == null)
                    {
                        var (locationName, address) = await ReverseGeocodeAsync(point.Latitude, point.Longitude);
                        var newTrip = new TripModel
                        {
                            FieldEngineerId = fieldEngineerId,
                            StartTime = point.Timestamp,
                            StartAddress = address, // Ginamit na natin ang result
                            Path = new List<LocationPoint> { point },
                            Distance = distance
                        };
                        _context.Trips.Add(newTrip);
                    }
                    else
                    {
                        ongoingTrip.Path.Add(point);
                        ongoingTrip.Distance += distance;
                    }
                }
                else
                {
                    if (ongoingTrip != null)
                    {
                        if ((point.Timestamp - ongoingTrip.Path.Last().Timestamp).TotalMinutes >= StationaryTimeMinutes)
                        {
                            if (ongoingTrip.Distance >= MinTripDistanceMiles)
                            {
                                var (locationName, address) = await ReverseGeocodeAsync(point.Latitude, point.Longitude);
                                ongoingTrip.EndTime = point.Timestamp;
                                ongoingTrip.EndAddress = address; // Ginamit na natin ang result
                            }
                            else
                            {
                                _context.Trips.Remove(ongoingTrip);
                            }
                        }
                    }
                }

                _context.LocationPoints.Add(point);
                await _context.SaveChangesAsync();
            }
        }
        
        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var r = 3958.8; // Radius of Earth in miles
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return r * c;
        }

        private double ToRadians(double angle) => (Math.PI / 180) * angle;

        // --- ITO YUNG CODE MO ---
        private async Task<(string LocationName, string Address)> ReverseGeocodeAsync(double lat, double lng)
        {
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

            return ($"Location near {lat:F3}, {lng:F3}", "Address not available");
        }
    }
}