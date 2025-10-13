using Microsoft.AspNetCore.Mvc;
using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace EcsFeMappingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LocationController : ControllerBase
    {
        private readonly AppDbContext _context;

        public LocationController(AppDbContext context)
        {
            _context = context;
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
                // Save location points to database
                _context.LocationPoints.AddRange(points);
                await _context.SaveChangesAsync();
                
                Console.WriteLine($"✅ Saved {points.Count} location points to database");

                return Ok(new { 
                    message = "Location points saved successfully", 
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
    }
}