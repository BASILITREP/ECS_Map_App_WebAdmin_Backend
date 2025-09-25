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

        //Drive Specific fields
        public double? DistanceKm { get; set; }
        public double? TopSpeedKmh { get; set; }

        //Stop Specific fields
        public string? LocationName { get; set; }
        public string? Address { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}