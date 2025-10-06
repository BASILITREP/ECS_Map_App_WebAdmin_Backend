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

            // Use the service to process the points
            await _tripService.ProcessLocationPoints(points);

            return Ok();
        }

        // Add a new endpoint to get trips
        [HttpGet("trips/{feId}")]
        public async Task<ActionResult<IEnumerable<TripModel>>> GetTrips(int feId)
        {
            return await _context.Trips
                .Include(t => t.Path)
                .Where(t => t.FieldEngineerId == feId)
                .OrderByDescending(t => t.StartTime)
                .ToListAsync();
        }
    }
}