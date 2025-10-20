using System;
using System.ComponentModel.DataAnnotations;

namespace EcsFeMappingApi.Models
{
    public enum EventType { Stop, Drive }

    public class ActivityEvent
    {
        public int Id { get; set; }
        public int FieldEngineerId { get; set; }
        public EventType Type { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int DurationMinutes { get; set; }
        public string? RoutePathJson { get; set; }

        //Drive Specific fields
        public double? DistanceKm { get; set; }
        public double? TopSpeedKmh { get; set; }
        public double? StartLatitude { get; set; }
        public double? StartLongitude { get; set; }
        public double? EndLatitude { get; set; }
        public double? EndLongitude { get; set; }
        public string? StartAddress { get; set; }
        public string? EndAddress { get; set; }

        //Stop Specific fields
        public string? LocationName { get; set; }
        public string? Address { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}