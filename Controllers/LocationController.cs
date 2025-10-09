// In Controllers/LocationController.cs

using Microsoft.AspNetCore.Mvc;
using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using EcsFeMappingApi.Services; // Add this using directive

namespace EcsFeMappingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LocationController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly TripDetectionService _tripService; // Add this

        // Inject the new service
        public LocationController(AppDbContext context, TripDetectionService tripService)
        {
            _context = context;
            _tripService = tripService;
        }

        // GET: api/Location/{feId}
        [HttpGet("{feId}")]
        public async Task<ActionResult<IEnumerable<LocationPoint>>> GetLocationHistory(int feId)
        {
            return await _context.LocationPoints
                                 .Where(p => p.FieldEngineerId == feId)
                                 .OrderByDescending(p => p.Timestamp)
                                 .ToListAsync();
        }

        // POST: api/Location
        [HttpPost]
        public async Task<IActionResult> PostLocationPoints(List<LocationPoint> points)
        {
            if (points == null || !points.Any())
            {
                return BadRequest("No location points provided.");
            }

            try 
            {
                // ADD THIS: Save location points to database first
                _context.LocationPoints.AddRange(points);
                await _context.SaveChangesAsync();
                
                Console.WriteLine($"✅ Saved {points.Count} location points to database");

                // THEN process for trip detection
                await _tripService.ProcessLocationPoints(points);
                
                Console.WriteLine($"✅ Processed {points.Count} points for trip detection");

                return Ok(new { 
                    message = "Location points saved and processed successfully", 
                    count = points.Count 
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error saving location points: {ex.Message}");
                Console.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                return StatusCode(500, $"Error processing location points: {ex.Message}");
            }
        }


        [HttpGet("trips/{fieldEngineerId}")]
        public async Task<ActionResult<IEnumerable<TripModel>>> GetFieldEngineerTrips(int fieldEngineerId)
        {
            try
            {
                var trips = await _context.Trips
                    .Where(t => t.FieldEngineerId == fieldEngineerId)
                    .Include(t => t.Path)
                    .OrderByDescending(t => t.StartTime)
                    .Take(50) // Limit to recent 50 trips
                    .ToListAsync();

                return Ok(trips);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching trips: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }
    }
}