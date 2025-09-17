using System.ComponentModel.DataAnnotations;

namespace EcsFeMappingApi.Models
{
    public class UserModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string? Username { get; set; }

        [Required]
        public byte[]? PasswordHash { get; set; }
        public string Role { get; set; } = "Admin";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
    }
}