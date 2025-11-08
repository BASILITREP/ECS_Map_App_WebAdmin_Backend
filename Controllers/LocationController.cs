using Microsoft.AspNetCore.Mvc;
using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using System.Collections.Generic;
using System.Linq;
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

        // ✅ GET: api/Location/{feId}
        [HttpGet("{feId}")]
        public async Task<ActionResult<IEnumerable<LocationPoint>>> GetLocationHistory(int feId)
        {
            var points = await _context.LocationPoints
                .Where(p => p.FieldEngineerId == feId)
                .OrderBy(p => p.Timestamp)
                .ToListAsync();

            // ✅ Always return 200 OK with an empty array if no points
            return Ok(points);
        }

        // ✅ POST: api/Location
        [HttpPost]
        public async Task<IActionResult> PostLocationPoints(List<LocationPoint> points)
        {
            if (points == null || !points.Any())
                return BadRequest("No location points provided.");

            foreach (var p in points)
            {
                if (p.FieldEngineerId <= 0)
                    return BadRequest("Each point must include a valid FieldEngineerId.");

                if (p.Timestamp == default)
                    p.Timestamp = DateTime.UtcNow;
            }

            _context.LocationPoints.AddRange(points);
            await _context.SaveChangesAsync();

            Console.WriteLine($"✅ Saved {points.Count} raw location points for FE #{points.First().FieldEngineerId}");

            // ✅ Removed old processor calls
            return Ok(new { message = "Location points saved successfully", count = points.Count });
        }
    }
}
