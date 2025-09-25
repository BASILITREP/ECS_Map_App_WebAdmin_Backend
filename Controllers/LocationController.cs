using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EcsFeMappingApi.Controllers
{
    public class LocationPointDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Speed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class LocationBatchDto
    {
        public int FieldEngineerId { get; set; }
        public List<LocationPointDto> Points { get; set; } = new List<LocationPointDto>();
    }

    [Route("api/[controller]")]
    [ApiController]
    public class LocationController : ControllerBase
    {
        private readonly AppDbContext _context;
        public LocationController(AppDbContext context)
        {
            _context = context;
        }

         [HttpPost("batch")]
        public async Task<IActionResult> PostBatch([FromBody] LocationBatchDto batchDto)
        {
            if (batchDto == null || batchDto.Points == null || batchDto.Points.Count == 0)
            {
                return BadRequest("Batch data is empty or invalid.");
            }

            var locationPoints = new List<LocationPoint>();
            foreach (var pointDto in batchDto.Points)
            {
                locationPoints.Add(new LocationPoint
                {
                    FieldEngineerId = batchDto.FieldEngineerId,
                    Latitude = pointDto.Latitude,
                    Longitude = pointDto.Longitude,
                    Speed = pointDto.Speed,
                    Timestamp = pointDto.Timestamp.ToUniversalTime() // Ensure UTC
                });
            }

            await _context.LocationPoints.AddRangeAsync(locationPoints);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"{locationPoints.Count} points received and saved." });
        }
    }
}