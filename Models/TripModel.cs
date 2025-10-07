using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EcsFeMappingApi.Models
{
    public class TripModel
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int FieldEngineerId { get; set; }
        public FieldEngineer FieldEngineer { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        // Original fields
        public string StartAddress { get; set; }
        public string EndAddress { get; set; }
        public double Distance { get; set; }

        // NEW FIELDS for enhanced trip tracking
        public double StartLatitude { get; set; }
        public double StartLongitude { get; set; }
        public double EndLatitude { get; set; }
        public double EndLongitude { get; set; }
        
        public string StartLocation { get; set; }  // Geocoded address
        public string EndLocation { get; set; }    // Geocoded address
        
        public string TripType { get; set; }       // "STATIONARY" or "MOVEMENT"
        public double TotalDistance { get; set; }  // Distance in km

        public virtual ICollection<LocationPoint> Path { get; set; } = new List<LocationPoint>();
    }
}