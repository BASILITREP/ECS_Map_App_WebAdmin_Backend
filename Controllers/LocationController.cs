using Microsoft.AspNetCore.Mvc;
using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using EcsFeMappingApi.Services; // ADD THIS

namespace EcsFeMappingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LocationController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ActivityProcessingService _activityProcessor; // ADD THIS

        // UPDATE THE CONSTRUCTOR
        public LocationController(AppDbContext context, ActivityProcessingService activityProcessor)
        {
            _context = context;
            _activityProcessor = activityProcessor; // ADD THIS
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
        // [HttpPost]
        // public async Task<IActionResult> PostLocationPoints(List<LocationPoint> points)
        // {
        //     if (points == null || !points.Any())
        //     {
        //         return BadRequest("No location points provided.");
        //     }

        //     try 
        //     {
        //         // Save location points to database
        //         _context.LocationPoints.AddRange(points);
        //         await _context.SaveChangesAsync();
                
        //         Console.WriteLine($"✅ Saved {points.Count} location points to database");

        //         // --- ADD THIS LINE TO TRIGGER PROCESSING ---
        //         await _activityProcessor.TriggerProcessingAsync();

        //         return Ok(new { 
        //             message = "Location points saved and processed successfully", 
        //             count = points.Count 
        //         });
        //     }
        //     catch (Exception ex)
        //     {
        //         Console.WriteLine($"❌ Error saving location points: {ex.Message}");
        //         Console.WriteLine($"❌ Stack trace: {ex.StackTrace}");
        //         return StatusCode(500, $"Error processing location points: {ex.Message}");
        //     }
        // }

        [HttpPost]
public async Task<IActionResult> PostLocationPoints([FromBody] List<LocationPoint> points)
{
    try
    {
        // 🧱 Validate incoming batch
        if (points == null || points.Count == 0)
        {
            Console.WriteLine("⚠️ Received empty location batch.");
            return BadRequest("No location points provided.");
        }

        var feId = points.FirstOrDefault()?.FieldEngineerId ?? 0;
        Console.WriteLine($"📦 Received {points.Count} points from FE #{feId}");

        // ✅ Normalize and prepare
        foreach (var p in points)
        {
            if (p.FieldEngineerId <= 0)
                return BadRequest("Each point must include a valid FieldEngineerId.");

            if (p.Timestamp == default)
                p.Timestamp = DateTime.UtcNow;

            p.IsProcessed = false; // ensure new points are actually picked up
        }

        // 💾 Save points to DB
        _context.LocationPoints.AddRange(points);
        await _context.SaveChangesAsync();
        Console.WriteLine($"✅ Saved {points.Count} location points to database");

        // ⚙️ Trigger processing safely
        try
        {
            await _activityProcessor.TriggerProcessingAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🔥 TriggerProcessingAsync crashed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        return Ok(new
        {
            message = "Location points saved and processing triggered successfully",
            count = points.Count
        });
    }
    catch (Exception ex)
    {
        // ❌ Top-level crash handling
        Console.WriteLine($"🔥 /api/Location crashed: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        return StatusCode(500, $"Error processing location points: {ex.Message}");
    }
}


    }
}