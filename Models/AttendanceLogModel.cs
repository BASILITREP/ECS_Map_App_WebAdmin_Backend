using System;
using System.ComponentModel.DataAnnotations;

namespace EcsFeMappingApi.Models
{
    public class AttendanceLogModel
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public int FieldEngineerId { get; set; }

        public DateTime TimeIn { get; set; } = DateTime.UtcNow;
        public DateTime? TimeOut { get; set; }

        public string? Location { get; set; }
        
    }
}