using System;
using System.ComponentModel.DataAnnotations;

namespace EcsFeMappingApi.Models
{
    public class FieldEngineer
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        
        
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public double CurrentLatitude { get; set; }
        public double CurrentLongitude { get; set; }
        public string? CurrentAddress { get; set; }
        public DateTime? TimeIn { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsActive { get; set; } = true;  // Add this too
        public string Status { get; set; } = string.Empty;
        public string? OneSignalPlayerId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? FcmToken { get; set; } = "e9bk85-wS2yzuRJIWbnfNs:APA91bGwjfBItKWEJ5r94i3meG6YqEIgCBDx1kxMfNa0yb57ZcOG7oViuyfmO17EE7-G4O26Te35L_IUvx19o3AFNfODerhb_BbVH0f1phD5PRS9-rRxOLs";
    }
}