using EcsFeMappingApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EcsFeMappingApi.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Branch> Branches { get; set; }
        public DbSet<FieldEngineer> FieldEngineers { get; set; }
        public DbSet<ServiceRequest> ServiceRequests { get; set; }
        public DbSet<UserModel> Users { get; set; } 
        public DbSet<ActivityEvent> ActivityEvents { get; set; }
        public DbSet<LocationPoint> LocationPoints { get; set; }
        public DbSet<AttendanceLogModel> AttendanceLogs { get; set; }
        

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure relationships
            modelBuilder.Entity<ServiceRequest>()
                .HasOne(sr => sr.Branch)
                .WithMany()
                .HasForeignKey(sr => sr.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ServiceRequest>()
                .HasOne(sr => sr.FieldEngineer)
                .WithMany()
                .HasForeignKey(sr => sr.FieldEngineerId)
                .OnDelete(DeleteBehavior.SetNull);
                


            modelBuilder.Entity<UserModel>().HasData(
                new UserModel
                {
                    Id = 1,
                    Username = "admin",
                    // This is "admin123" hashed - in production use a proper password hasher
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                    Role = "Admin"
                }
            );
        }
    }
}