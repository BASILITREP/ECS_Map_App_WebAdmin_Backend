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
        }
    }
}