using Microsoft.AspNetCore.Mvc;
using EcsFeMappingApi.Services;
using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace EcsFeMappingApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CoordinatesController : ControllerBase
    {
        private readonly NotificationService _notificationService;
        private readonly AppDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public CoordinatesController(
            NotificationService notificationService, 
            AppDbContext context,
            IHubContext<NotificationHub> hubContext)
        {
            _notificationService = notificationService;
            _context = context;
            _hubContext = hubContext;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendCoordinates([FromBody] CoordinateRequest request)
        {
            try
            {
                Console.WriteLine($"Received coordinates: Lat: {request.Latitude}, Lng: {request.Longitude}, userId: {request.userId}");
                
                // Check if this is a field engineer by userId
                if (request.userId.HasValue && request.userId.Value > 0)
                {
                    // This is a field engineer, not the boss
                    var fieldEngineer = await _context.FieldEngineers
                        .FirstOrDefaultAsync(fe => fe.Id == request.userId.Value);

                    if (fieldEngineer != null)
                    {
                        // Update existing field engineer location - SAME AS updateLocation endpoint
                        fieldEngineer.CurrentLatitude = request.Latitude;
                        fieldEngineer.CurrentLongitude = request.Longitude;
                        fieldEngineer.UpdatedAt = DateTime.UtcNow;
                        fieldEngineer.Status = "Active";
                        fieldEngineer.IsAvailable = true;

                        await _context.SaveChangesAsync();

                        // 🔥 USE THE SAME SIGNALR EVENT AS updateLocation endpoint
                        await _hubContext.Clients.All.SendAsync("ReceiveFieldEngineerUpdate", fieldEngineer);
                        
                        Console.WriteLine($"✅ Field Engineer {fieldEngineer.Name} location updated via coordinates/send");
                        
                        return Ok(new { 
                            message = "Field engineer coordinates updated successfully", 
                            timestamp = DateTime.UtcNow,
                            engineer = fieldEngineer,
                            type = "field_engineer_update"
                        });
                    }
                    else
                    {
                        // Field engineer not found, create from boss's API data
                        var newFieldEngineer = new FieldEngineer
                        {
                            Id = request.userId.Value,
                            Name = $"User {request.userId.Value}",
                            FirstName = "Unknown",
                            LastName = "User", 
                            Email = $"user{request.userId.Value}@company.com",
                            Phone = "000-000-0000",
                            Status = "Active",
                            IsAvailable = true,
                            IsActive = true,
                            CurrentLatitude = request.Latitude,
                            CurrentLongitude = request.Longitude,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        
                        _context.FieldEngineers.Add(newFieldEngineer);
                        await _context.SaveChangesAsync();

                        // 🔥 USE THE SAME SIGNALR EVENT AS updateLocation endpoint
                        await _hubContext.Clients.All.SendAsync("ReceiveFieldEngineerUpdate", newFieldEngineer);
                        
                        Console.WriteLine($"✅ New Field Engineer created from boss API: {newFieldEngineer.Name}");
                        
                        return Ok(new { 
                            message = "New field engineer created and coordinates updated", 
                            timestamp = DateTime.UtcNow,
                            engineer = newFieldEngineer,
                            type = "field_engineer_created"
                        });
                    }
                }
                else
                {
                    // No userId provided, treat as boss location (original logic)
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

                    // Broadcast as boss coordinates (red marker)
                    await _notificationService.BroadcastCoordinateUpdate(new
                    {
                        type = "boss_location",
                        engineer = engineer,
                        latitude = request.Latitude,
                        longitude = request.Longitude,
                        description = request.Description,
                        timestamp = DateTime.UtcNow,
                        source = "boss",
                    });
                    
                    return Ok(new { 
                        message = "Boss coordinates saved and broadcasted successfully", 
                        timestamp = DateTime.UtcNow,
                        engineer = engineer,
                        coordinates = request
                    });
                }
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