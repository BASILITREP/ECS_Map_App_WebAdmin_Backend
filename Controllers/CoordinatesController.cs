using Microsoft.AspNetCore.Mvc;
using EcsFeMappingApi.Services;

namespace EcsFeMappingApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CoordinatesController : ControllerBase
    {
        private readonly NotificationService _notificationService;

        public CoordinatesController(NotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendCoordinates([FromBody] CoordinateRequest request)
        {
            try
            {
                // Log the received coordinates
                Console.WriteLine($"Received coordinates from boss: Lat: {request.Latitude}, Lng: {request.Longitude}");
                
                // Broadcast to all connected clients via SignalR
                await _notificationService.BroadcastCoordinateUpdate(request);
                
                return Ok(new { 
                    message = "Coordinates sent successfully", 
                    timestamp = DateTime.UtcNow,
                    coordinates = request
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new { 
                message = "Coordinates API is working!", 
                timestamp = DateTime.UtcNow,
                server = "Railway" 
            });
        }
    }

    public class CoordinateRequest
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; } = "location_update";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}