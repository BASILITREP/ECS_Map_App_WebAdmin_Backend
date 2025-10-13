using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using EcsFeMappingApi.Services;
    
namespace EcsFeMappingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FieldEngineerController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public FieldEngineerController(AppDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet("{id}/activity")]
        public async Task<ActionResult<IEnumerable<ActivityEvent>>> GetFieldEngineerActivity(int id)
        {
            var activities = await _context.ActivityEvents
                .Where(e => e.FieldEngineerId == id)
                .OrderByDescending(e => e.StartTime)
                .Take(50) // Optionally limit to the last 50 events for performance
                .ToListAsync();

            if (activities == null)
            {
                return NotFound();
            }

            return Ok(activities);
        }

        // GET: api/FieldEngineers
        [HttpGet]
        public async Task<ActionResult<IEnumerable<FieldEngineer>>> GetFieldEngineers()
        {
            return await _context.FieldEngineers.ToListAsync();
        }

        // GET: api/FieldEngineers/5
        [HttpGet("{id}")]
        public async Task<ActionResult<FieldEngineer>> GetFieldEngineer(int id)
        {
            var fieldEngineer = await _context.FieldEngineers.FindAsync(id);

            if (fieldEngineer == null)
            {
                return NotFound();
            }

            return fieldEngineer;
        }

        // POST: api/FieldEngineers
        [HttpPost]
        public async Task<ActionResult<FieldEngineer>> PostFieldEngineer(FieldEngineer fieldEngineer)
        {
            Console.WriteLine($"Creating new field engineer: {fieldEngineer.Name}");

            _context.FieldEngineers.Add(fieldEngineer);
            var result = await _context.SaveChangesAsync();

            Console.WriteLine($"Database changes saved: {result} rows affected");
            Console.WriteLine($"Field engineer ID after save: {fieldEngineer.Id}");

            await _hubContext.Clients.All.SendAsync("ReceiveNewFieldEngineer", fieldEngineer);
            Console.WriteLine("SignalR notification sent for new field engineer");

            return CreatedAtAction("GetFieldEngineer", new { id = fieldEngineer.Id }, fieldEngineer);
        }

        // PUT: api/FieldEngineers/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutFieldEngineer(int id, FieldEngineer fieldEngineer)
        {
            if (id != fieldEngineer.Id)
            {
                return BadRequest();
            }

            fieldEngineer.UpdatedAt = DateTime.UtcNow;
            _context.Entry(fieldEngineer).State = EntityState.Modified;
            _context.Entry(fieldEngineer).Property(x => x.CreatedAt).IsModified = false;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!FieldEngineerExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // PATCH: api/FieldEngineer/{id}/location
        [HttpPatch("{id}/location")]
        public async Task<IActionResult> UpdateLocation(int id, [FromBody] UpdateLocationDto dto)
        {
            var fe = await _context.FieldEngineers.FindAsync(id);
            if (fe == null)
            {
                return NotFound();
            }

            fe.CurrentLatitude = dto.Latitude;
            fe.CurrentLongitude = dto.Longitude;
            if (dto.IsAvailable.HasValue)
            {
                fe.IsAvailable = dto.IsAvailable.Value;
                fe.Status = dto.IsAvailable.Value ? "Active" : fe.Status;
            }
            fe.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Location updated" });
        }

        // DELETE: api/FieldEngineers/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteFieldEngineer(int id)
        {
            var fieldEngineer = await _context.FieldEngineers.FindAsync(id);
            if (fieldEngineer == null)
            {
                return NotFound();
            }

            _context.FieldEngineers.Remove(fieldEngineer);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // GET: api/FieldEngineer/{id}/current-assignment
        [HttpGet("{id}/current-assignment")]
        public async Task<IActionResult> GetCurrentAssignment(int id)
        {
            var sr = await _context.ServiceRequests
                .Include(s => s.Branch)
                .Include(s => s.FieldEngineer)
                .Where(s => s.FieldEngineerId == id && s.Status == "accepted")
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefaultAsync();

            if (sr == null) return Ok(null);

            return Ok(ServiceRequestDto.FromEntity(sr, sr.FieldEngineer));
        }

        // POST: api/FieldEngineers/updateLocation
        [HttpPost("updateLocation")]
        public async Task<IActionResult> UpdateLocation([FromBody] LocationUpdateDto locationUpdate)
        {
            try
            {
                // Find the field engineer by ID
                var engineer = await _context.FieldEngineers.FindAsync(locationUpdate.Id);
                if (engineer == null)
                {
                    return NotFound("Field engineer not found");
                }

                // Update the location
                engineer.CurrentLatitude = locationUpdate.CurrentLatitude;
                engineer.CurrentLongitude = locationUpdate.CurrentLongitude;
                engineer.UpdatedAt = DateTime.Now;

                // Save changes
                await _context.SaveChangesAsync();

                // Notify all clients about the update
                await _hubContext.Clients.All.SendAsync("ReceiveFieldEngineerUpdate", engineer);

                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("{fieldEngineerId}/activity")]
        public async Task<IActionResult> GetFieldEngineerActivity(int fieldEngineerId)
        {
            var engineerExists = await _context.FieldEngineers.AnyAsync(fe => fe.Id == fieldEngineerId);
            if (!engineerExists)
            {
                return NotFound($"Field engineer with ID {fieldEngineerId} not found.");
            }

            var activities = await _context.ActivityEvents
                .Where(a => a.FieldEngineerId == fieldEngineerId)
                .OrderByDescending(a => a.StartTime)
                .ToListAsync();

            return Ok(activities);
        }

        [HttpPost("loginsync")]
public async Task<ActionResult<FieldEngineer>> LoginSync([FromBody] LoginSyncRequest request)
{
    try
    {
        // Find the Field Engineer using UserId from external API
        var fieldEngineer = await _context.FieldEngineers
            .FirstOrDefaultAsync(fe => fe.Id == request.UserId);

        if (fieldEngineer == null)
        {
            // User doesn't exist in our system, so create them with data from boss's API
            fieldEngineer = new FieldEngineer
            {
                Id = request.UserId,
                Name = $"{request.FirstName} {request.LastName}",
                FirstName = request.FirstName,
                LastName = request.LastName,
                FcmToken = request.FcmToken,
                CurrentLatitude = 0.0,  // Default location
                CurrentLongitude = 0.0, // Default location
                IsActive = true,
                IsAvailable = true,
                Status = "Online",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.FieldEngineers.Add(fieldEngineer);
            Console.WriteLine($"Creating new field engineer from external API: {fieldEngineer.Name} (ID: {fieldEngineer.Id})");
        }
        else
        {
            // User exists, update their info and FCM token
            fieldEngineer.FirstName = request.FirstName ?? fieldEngineer.FirstName;
            fieldEngineer.LastName = request.LastName ?? fieldEngineer.LastName;
            fieldEngineer.Name = $"{fieldEngineer.FirstName} {fieldEngineer.LastName}";
            fieldEngineer.FcmToken = request.FcmToken;
            fieldEngineer.IsAvailable = true; // Make available upon login
            fieldEngineer.Status = "Online";
            fieldEngineer.UpdatedAt = DateTime.UtcNow;

            _context.FieldEngineers.Update(fieldEngineer);
            Console.WriteLine($"Updating existing field engineer: {fieldEngineer.Name} (ID: {fieldEngineer.Id})");
        }

        await _context.SaveChangesAsync();

        // Send SignalR notification
        await _hubContext.Clients.All.SendAsync("ReceiveFieldEngineerUpdate", fieldEngineer);

        // Return the complete, updated profile to the app
        return Ok(fieldEngineer);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Login sync error: {ex.Message}");
        return StatusCode(500, $"Login sync failed: {ex.Message}");
    }
}

        private bool FieldEngineerExists(int id)
        {
            return _context.FieldEngineers.Any(e => e.Id == id);
        }
    }



    public class UpdateLocationDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool? IsAvailable { get; set; }
    }


    public class LocationUpdateDto
    {
        public int Id { get; set; }
        public double CurrentLatitude { get; set; }
        public double CurrentLongitude { get; set; }
    }

    public class LoginSyncRequest
    {
        public int UserId { get; set; }
        public string? FirstName { get; set; }  // Add these
        public string? LastName { get; set; }   // Add these
        public string FcmToken { get; set; }
    }
    
}