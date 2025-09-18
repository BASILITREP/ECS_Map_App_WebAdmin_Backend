using Microsoft.AspNetCore.Mvc;
using EcsFeMappingApi.Services;
using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EcsFeMappingApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CoordinatesController : ControllerBase
    {
        private readonly NotificationService _notificationService;
        private readonly AppDbContext _context;

        public CoordinatesController(NotificationService notificationService, AppDbContext context)
        {
            _notificationService = notificationService;
            _context = context;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendCoordinates([FromBody] CoordinateRequest request)
        {
            try
            {
                Console.WriteLine($"Boss sent coordinates: Lat: {request.Latitude}, Lng: {request.Longitude}");
            
                var engineer = await _context.FieldEngineers
                    .FirstOrDefaultAsync(fe => fe.Email == "boss@company.com");

                if (engineer == null)
                {
                    // Create boss entry
                    engineer = new FieldEngineer
                    {
                        Name = "Boss",
                        Email = "boss@company.com",
                        Phone = "000-000-0000",
                        Status = "Active",
                        IsAvailable = true,
                        CurrentLatitude = request.Latitude,
                        CurrentLongitude = request.Longitude,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.FieldEngineers.Add(engineer);
                }
                else
                {
                    // Update boss location
                    engineer.CurrentLatitude = request.Latitude;
                    engineer.CurrentLongitude = request.Longitude;
                    engineer.UpdatedAt = DateTime.UtcNow;
                    engineer.Status = "Active";
                }

                await _context.SaveChangesAsync();

                // Broadcast to all connected clients
                await _notificationService.BroadcastCoordinateUpdate(new {
                    type = "boss_location",
                    engineer = engineer,
                    latitude = request.Latitude,
                    longitude = request.Longitude,
                    description = request.Description,
                    timestamp = DateTime.UtcNow,
                    source = "boss"
                });
                
                return Ok(new { 
                    message = "Boss coordinates saved and broadcasted successfully", 
                    timestamp = DateTime.UtcNow,
                    engineer = engineer,
                    coordinates = request
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("test")]
        public async Task<IActionResult> Test()
        {
            try
            {
                var engineerCount = await _context.FieldEngineers.CountAsync();
                var branchCount = await _context.Branches.CountAsync();
                
                return Ok(new { 
                    message = "Railway database connection working!", 
                    timestamp = DateTime.UtcNow,
                    server = "Railway",
                    database = "Connected",
                    engineers = engineerCount,
                    branches = branchCount
                });
            }
            catch (Exception ex)
            {
                return Ok(new { 
                    message = "API working but database not connected", 
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }
    }

    public class CoordinateRequest
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? Description { get; set; } = "Boss location";
        public string? Type { get; set; } = "boss_update";
        public int? userId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}