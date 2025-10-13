using System;
using System.ComponentModel.DataAnnotations;

namespace EcsFeMappingApi.Models
{
    public class LocationPoint
    {
        [Key]
        public int Id { get; set; }
        public int FieldEngineerId { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Speed { get; set; } // Speed in meters per second
        public DateTime Timestamp { get; set; }
        public bool IsProcessed { get; set; } = false; // ADD THIS LINE
    }
}