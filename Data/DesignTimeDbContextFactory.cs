using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace EcsFeMappingApi.Data
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var builder = new DbContextOptionsBuilder<AppDbContext>();
            
            // Use local connection string for design-time/migrations
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            
            builder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

            return new AppDbContext(builder.Options);
        }
    }
}