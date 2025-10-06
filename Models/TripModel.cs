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

        public string StartAddress { get; set; }
        public string EndAddress { get; set; }

        public double Distance { get; set; }

        public virtual ICollection<LocationPoint> Path { get; set; } = new List<LocationPoint>();
    }
}