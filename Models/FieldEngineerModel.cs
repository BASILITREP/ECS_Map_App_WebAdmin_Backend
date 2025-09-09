using System;
using System.ComponentModel.DataAnnotations;

namespace EcsFeMappingApi.Models
{
    public class FieldEngineer
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public double? CurrentLatitude { get; set; }
        public double? CurrentLongitude { get; set; }
        public bool IsAvailable { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? OneSignalPlayerId { get; set; } // For push notification targeting
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}