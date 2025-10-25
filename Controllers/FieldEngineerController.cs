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
using System.Text.Json;
    
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

            // 🧭 Reverse geocode the current location to human-readable address
            var address = await ReverseGeocodeAsync(dto.Latitude, dto.Longitude);
            fe.CurrentAddress = address;

            // Only set TimeIn if it's not yet set (clocked in)
            if (!fe.TimeIn.HasValue)
            {
                fe.TimeIn = DateTime.UtcNow;
            }

            fe.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

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
                engineer.UpdatedAt = DateTime.UtcNow;

                // 🔥 Dynamically determine and recover status
                var newStatus = DetermineStatus(engineer);
                if (engineer.Status != newStatus)
                {
                    engineer.Status = newStatus;
                    Console.WriteLine($"Status updated for FE #{engineer.Id}: {newStatus}");
                }


                // Save changes
                await _context.SaveChangesAsync();

                // Notify all clients about the update
                Console.WriteLine("📡 Sending SignalR broadcast for FE update...");
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
                .Take(100) // Limit to the last 100 activities for good performance
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
                        Status = "Active",
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
                    fieldEngineer.Status = "Active";
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

        private async Task<string> ReverseGeocodeAsync(double lat, double lon)
        {
            var http = new HttpClient();
            var apiKey = "pk.eyJ1IjoiYmFzaWwxLTIzIiwiYSI6ImNtZWFvNW43ZTA0ejQycHBtd3dkMHJ1bnkifQ.Y-IlM-vQAlaGr7pVQnug3Q";
            var url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lon},{lat}.json?access_token={apiKey}&types=address,poi";

            try
            {
                var response = await http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var features = doc.RootElement.GetProperty("features");
                    if (features.GetArrayLength() > 0)
                    {
                        var feature = features[0];
                        return feature.GetProperty("place_name").GetString() ?? "Unknown location";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Reverse geocode failed: {ex.Message}");
            }

            return "Unknown location";
        }

        private string DetermineStatus(FieldEngineer fe)
        {
            var now = DateTime.UtcNow;

            // 1️⃣ Not clocked in → Inactive
            if (fe.TimeIn == null)
                return "Inactive";

            // 2️⃣ Clocked in, check if they’re updating
            var minutesSinceUpdate = (now - fe.UpdatedAt).TotalMinutes;

            if (minutesSinceUpdate <= 2)
                return "Active"; // online & sending updates
            else
                return "Location Off"; // clocked in but no updates for >2min
        }


        // POST: api/FieldEngineer/{id}/clockin
        [HttpPost("{id}/clockin")]
        public async Task<IActionResult> ClockIn(int id)
        {
            var engineer = await _context.FieldEngineers.FindAsync(id);
            if (engineer == null) return NotFound("Engineer not found");

            // Prevent double Time In without Time Out
            var existingLog = await _context.AttendanceLogs
                .Where(l => l.FieldEngineerId == id && l.TimeOut == null)
                .FirstOrDefaultAsync();

            if (existingLog != null)
                return BadRequest("Already clocked in, please clock out first.");

            var log = new AttendanceLogModel
            {
                FieldEngineerId = id,
                TimeIn = DateTime.UtcNow,
                Location = engineer.CurrentAddress
            };

            _context.AttendanceLogs.Add(log);
            engineer.TimeIn = log.TimeIn; // optional mirror field
            engineer.Status = "Active";
            engineer.UpdatedAt = DateTime.UtcNow; // ✅ Fix
            await _context.SaveChangesAsync();

            // 📡 Notify dashboard in real-time
            await _hubContext.Clients.All.SendAsync("ReceiveFieldEngineerUpdate", engineer);

            return Ok(new { message = "Clocked in successfully", log });
        }

        // POST: api/FieldEngineer/{id}/clockout
[HttpPost("{id}/clockout")]
public async Task<IActionResult> ClockOut(int id)
{
    var engineer = await _context.FieldEngineers.FindAsync(id);
    if (engineer == null) return NotFound("Engineer not found");

    var log = await _context.AttendanceLogs
        .Where(l => l.FieldEngineerId == id && l.TimeOut == null)
        .OrderByDescending(l => l.TimeIn)
        .FirstOrDefaultAsync();

    if (log == null)
        return BadRequest("No active Time In found for this engineer.");

    // ✅ End the current session
    log.TimeOut = DateTime.UtcNow;

    // ✅ Update FieldEngineer properties
    engineer.Status = "Inactive";
    engineer.IsAvailable = false;
    engineer.UpdatedAt = DateTime.UtcNow;
    engineer.TimeIn = null; // Clear TimeIn on clock out

    await _context.SaveChangesAsync();

    // ✅ 🔥 Real-time update to web admin via SignalR
    await _hubContext.Clients.All.SendAsync("ReceiveFieldEngineerUpdate", engineer);

    return Ok(new
    {
        message = "Clocked out successfully",
        log,
        engineer.Status,
        engineer.UpdatedAt
    });
}


        // GET: api/FieldEngineer/{id}/attendance
        [HttpGet("{id}/attendance")]
        public async Task<IActionResult> GetAttendanceLogs(int id)
        {
            var logs = await _context.AttendanceLogs
                .Where(l => l.FieldEngineerId == id)
                .OrderByDescending(l => l.TimeIn)
                .ToListAsync();

            return Ok(logs);
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