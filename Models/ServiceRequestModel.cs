using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EcsFeMappingApi.Models
{
    public class ServiceRequest
    {
        [Key]
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lng { get; set; }
        
        [ForeignKey("Branch")]
        public int BranchId { get; set; }
        public virtual Branch Branch { get; set; } = null!;
        
        [ForeignKey("FieldEngineer")]
        public int? FieldEngineerId { get; set; }
        public virtual FieldEngineer? FieldEngineer { get; set; }
        
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public double CurrentRadiusKm { get; set; } = 5; // Default radius
    }

    public class ServiceRequestCreateDto
    {
        public int BranchId { get; set; }
    }

    public class ServiceRequestDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lng { get; set; }
        public int BranchId { get; set; }
        public int? FieldEngineerId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? AcceptedAt { get; set; }

        // Add the FromEntity method
        public static ServiceRequestDto FromEntity(ServiceRequest entity, FieldEngineer? fieldEngineer = null)
        {
            return new ServiceRequestDto
            {
                Id = entity.Id,
                Title = entity.Title,
                Description = entity.Description,
                Status = entity.Status,
                Priority = entity.Priority,
                BranchName = entity.BranchName,
                Lat = entity.Lat,
                Lng = entity.Lng,
                BranchId = entity.BranchId,
                FieldEngineerId = fieldEngineer?.Id ?? entity.FieldEngineerId,
                CreatedAt = entity.CreatedAt,
                AcceptedAt = entity.AcceptedAt
            };
        }
    }
}