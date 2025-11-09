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
using Microsoft.Extensions.Logging; // ✅ ADD THIS

namespace EcsFeMappingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FieldEngineerController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly FirebaseMessagingService _firebaseService;
        private readonly ILogger<FieldEngineerController> _logger; // ✅ ADD THIS

        public FieldEngineerController(
            AppDbContext context, 
            IHubContext<NotificationHub> hubContext, 
            FirebaseMessagingService firebaseService,
            ILogger<FieldEngineerController> logger) // ✅ ADD THIS
        {
            _context = context;
            _hubContext = hubContext;
            _firebaseService = firebaseService;
            _logger = logger; // ✅ ADD THIS
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
                fe.TimeIn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
    TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore"));

            }

            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
fe.UpdatedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);

            await _context.SaveChangesAsync();

            if (dto.IsAvailable.HasValue)
            {
                fe.IsAvailable = dto.IsAvailable.Value;
                fe.Status = dto.IsAvailable.Value ? "Active" : fe.Status;
            }
       
fe.UpdatedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);

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

                // ✅ Reverse geocode for address
                try
                {
                    var address = await ReverseGeocodeAsync(locationUpdate.CurrentLatitude, locationUpdate.CurrentLongitude);
                    engineer.CurrentAddress = string.IsNullOrWhiteSpace(address) ? "Unknown" : address;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Reverse geocode failed: {ex.Message}");
                    engineer.CurrentAddress = "Unknown";
                }

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
                    var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
var phNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);

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
                        Status = "Logged In",
                        CreatedAt = phNow,
                        UpdatedAt = phNow
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
                    fieldEngineer.IsActive = true;
                    fieldEngineer.Status = "Logged In";
                    fieldEngineer.UpdatedAt = phNow;

                    _context.FieldEngineers.Update(fieldEngineer);
                    Console.WriteLine($"Updating existing field engineer: {fieldEngineer.Name} (ID: {fieldEngineer.Id})");
                }

                await _context.SaveChangesAsync();

                // Send SignalR notification
                await _hubContext.Clients.All.SendAsync("ReceiveFieldEngineerUpdate", fieldEngineer);

                // 📱 Send Firebase Push Notification to the Field Engineer
                if (!string.IsNullOrEmpty(fieldEngineer.FcmToken))
                {
                    await _firebaseService.SendNotificationAsync(
                        "doroti-fe", // your Firebase projectId
                        fieldEngineer.FcmToken,
                        "Welcome Back, " + fieldEngineer.FirstName,
                        "You have successfully logged in to DOROTI."
                    );
                }



                Console.WriteLine($"🟢 FE #{fieldEngineer.Id} logged in: {fieldEngineer.Name} at {DateTime.UtcNow}");
                _ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(60));

        // ✅ Create an independent service scope
        using var scope = EcsFeMappingApi.Program.ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firebase = scope.ServiceProvider.GetRequiredService<FirebaseMessagingService>();

        var engineerCheck = await db.FieldEngineers.FirstOrDefaultAsync(f => f.Id == fieldEngineer.Id);
        if (engineerCheck != null)
        {
            var hasClockedIn = await db.AttendanceLogs
                .AnyAsync(l => l.FieldEngineerId == engineerCheck.Id && l.TimeOut == null);

            if (!hasClockedIn)
            {
                Console.WriteLine($"⏰ FE #{engineerCheck.Id} has not clocked in after 60s — sending reminder...");
                if (!string.IsNullOrEmpty(engineerCheck.FcmToken))
                {
                    await firebase.SendNotificationAsync(
                        "doroti-fe",
                        engineerCheck.FcmToken,
                        "Clock In Reminder ⏰",
                        $"Hey {engineerCheck.FirstName}, don’t forget to clock in!"
                    );
                }
            }
            else
            {
                Console.WriteLine($"✅ FE #{engineerCheck.Id} already clocked in, no reminder sent.");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ 60-second clock-in reminder failed: {ex.Message}");
    }
});

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

            // 🟦 Preserve Logged In state if user is logged in but hasn't clocked in
            if (fe.TimeIn == null)
            {
                if (fe.Status == "Logged In")
                    return "Logged In";
                if (fe.Status == "Off-work")
                    return "Off-work";
                return "Inactive";
            }

            // 🟢 If clocked in and still sending updates → Active
            var minutesSinceUpdate = (now - fe.UpdatedAt).TotalMinutes;
            if (minutesSinceUpdate <= 2)
                return "Active";

            // 🟡 Clocked in but no update in >2min → Location Off
            return "Location Off";
        }

        [HttpPost("{id}/login-sync")]
        public async Task<IActionResult> LoginSync(int id, [FromBody] FieldEngineer updatedData)
        {
            var fieldEngineer = await _context.FieldEngineers.FindAsync(id);
            if (fieldEngineer == null)
                return NotFound("Field Engineer not found");

            // ✅ Update basic login state
            fieldEngineer.Status = "Logged In";
            fieldEngineer.IsActive = true;
            fieldEngineer.IsAvailable = true;
            fieldEngineer.UpdatedAt = DateTime.UtcNow;
            fieldEngineer.FcmToken = updatedData.FcmToken; // Update FCM token if provided

            await _context.SaveChangesAsync();

            // ✅ Send SignalR broadcast to web dashboard
            await _hubContext.Clients.All.SendAsync("ReceiveFieldEngineerUpdate", fieldEngineer);

            // ✅ Optional: Log to console for debugging
            Console.WriteLine($"🟢 FE #{fieldEngineer.Id} logged in: {fieldEngineer.Name} at {DateTime.UtcNow}");

            _ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(60));

        // ✅ Create an independent service scope
        using var scope = EcsFeMappingApi.Program.ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firebase = scope.ServiceProvider.GetRequiredService<FirebaseMessagingService>();

        var engineerCheck = await db.FieldEngineers.FirstOrDefaultAsync(f => f.Id == fieldEngineer.Id);
        if (engineerCheck != null)
        {
            var hasClockedIn = await db.AttendanceLogs
                .AnyAsync(l => l.FieldEngineerId == engineerCheck.Id && l.TimeOut == null);

            if (!hasClockedIn)
            {
                Console.WriteLine($"⏰ FE #{engineerCheck.Id} has not clocked in after 60s — sending reminder standby...");
                if (!string.IsNullOrEmpty(engineerCheck.FcmToken))
                {
                    await firebase.SendNotificationAsync(
                        "doroti-fe",
                        engineerCheck.FcmToken,
                        "Clock In Reminder ⏰",
                        $"Hey {engineerCheck.FirstName}, don’t forget to clock in please!"
                    );
                }
            }
            else
            {
                Console.WriteLine($"✅ FE #{engineerCheck.Id} already clocked in, no reminder sent.");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ 60-second clock-in reminder failed: {ex.Message}");
    }
});


            return Ok(new { message = "Login successful", fieldEngineer });
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

            // ✅ Use PH (UTC+8) time instead of UTC
            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            var phNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);

            var log = new AttendanceLogModel
            {
                FieldEngineerId = id,
                TimeIn = phNow,
                Location = engineer.CurrentAddress
            };

            _context.AttendanceLogs.Add(log);

            engineer.TimeIn = log.TimeIn; // Mirror TimeIn
            engineer.Status = "Active";
            engineer.UpdatedAt = phNow;
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

            // ✅ Use PH local time for clock-out
            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            var phNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);

            // ✅ End the current session
            log.TimeOut = phNow;

            // ✅ Update FieldEngineer properties
            engineer.Status = "Off-work";
            engineer.IsAvailable = false;
            engineer.UpdatedAt = phNow;

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

            // ✅ Convert each log’s time to PH local time before sending
            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");

            var localizedLogs = logs.Select(l => new
            {
                l.Id,
                l.FieldEngineerId,
                TimeIn = TimeZoneInfo.ConvertTimeFromUtc(l.TimeIn, phTimeZone),
                TimeOut = l.TimeOut.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(l.TimeOut.Value, phTimeZone)
                    : (DateTime?)null,
                l.Location
            });

            return Ok(localizedLogs);
        }


        // POST: api/FieldEngineer/{id}/logout
        [HttpPost("{id}/logout")]
        public async Task<IActionResult> Logout(int id)
        {
            var engineer = await _context.FieldEngineers.FindAsync(id);
            if (engineer == null) return NotFound("Engineer not found");

            // ✅ Convert to PH local time
            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            var phNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);

            engineer.Status = "Inactive";
            engineer.IsAvailable = false;
            engineer.UpdatedAt = phNow;

            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("ReceiveFieldEngineerUpdate", engineer);

            return Ok(new
            {
                message = "Logged out successfully",
                engineer.Status,
                engineer.UpdatedAt
            });
        }


        


        // [HttpPost("test-fcm")]
        // public async Task<IActionResult> TestFirebase()
        // {
        //     try
        //     {
        //         await _firebaseService.SendNotificationAsync(
        //             "doroti-fe",
        //             "eKw0GPmPTmOxJdi_zUEb3s:APA91bHLFl6lNcl1bditGsTnYu1sNQD2FMBuhrpwFY2rlO0W9-4bfWqI79KKXMf8NRrufTxlk4OuKW0gtV40qr15y2zYXbg0zX7OPq8TiFeiT9ToNlC92PM",
        //             "Test from Railway 🚀",
        //             "This is a direct test message from your backend."
        //         );

        //         return Ok(new { message = "✅ FCM test message sent!" });
        //     }
        //     catch (Exception ex)
        //     {
        //         Console.WriteLine($"❌ FCM test failed: {ex.Message}");
        //         return StatusCode(500, new { error = ex.Message });
        //     }
        // }
        
        // GET: api/FieldEngineer/{engineerId}/history?minStayMinutes=10
        [HttpGet("{engineerId}/history")]
        public async Task<ActionResult<IEnumerable<ActivityEvent>>> GetActivityHistory(
            int engineerId,
            [FromQuery] int? minStayMinutes = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var engineerExists = await _context.FieldEngineers.AnyAsync(fe => fe.Id == engineerId);
                if (!engineerExists)
                {
                    return NotFound($"Field engineer with ID {engineerId} not found.");
                }

                var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
                var nowPh = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);

                IQueryable<ActivityEvent> query = _context.ActivityEvents
                    .Where(e => e.FieldEngineerId == engineerId);

                // ✅ Apply date range filter
                if (startDate.HasValue)
                {
                    var startDateUtc = TimeZoneInfo.ConvertTimeToUtc(startDate.Value.Date, phTimeZone);
                    query = query.Where(e => e.StartTime >= startDateUtc);
                }

                if (endDate.HasValue)
                {
                    var endDateUtc = TimeZoneInfo.ConvertTimeToUtc(endDate.Value.Date.AddDays(1), phTimeZone);
                    query = query.Where(e => e.StartTime < endDateUtc);
                }

                // ✅ Default to TODAY if no dates specified
                if (!startDate.HasValue && !endDate.HasValue)
                {
                    var todayPh = nowPh.Date;
                    var tomorrowPh = todayPh.AddDays(1);
                    var todayUtc = TimeZoneInfo.ConvertTimeToUtc(todayPh, phTimeZone);
                    var tomorrowUtc = TimeZoneInfo.ConvertTimeToUtc(tomorrowPh, phTimeZone);
                    
                    query = query.Where(e => e.StartTime >= todayUtc && e.StartTime < tomorrowUtc);
                }

                var allEvents = await query.OrderBy(e => e.StartTime).ToListAsync();

                // ✅ If NO stay filter, return all events
                if (!minStayMinutes.HasValue || minStayMinutes.Value <= 0)
                {
                    _logger.LogInformation($"✅ Fetched {allEvents.Count} activities (no stay filter) for FE #{engineerId}");
                    return Ok(allEvents);
                }

                // 🔥 NEW LOGIC: Clock-in point (Point A) is ALWAYS included
                var clockInPoint = allEvents
                    .Where(e => e.Type == EventType.Stop)
                    .OrderBy(e => e.StartTime)
                    .FirstOrDefault();

                if (clockInPoint == null)
                {
                    _logger.LogWarning($"⚠️ No clock-in point found for FE #{engineerId}");
                    return Ok(new List<ActivityEvent>());
                }

                // ✅ Filter destination stays (Points B, C, D...) by duration
                var validDestinationStays = allEvents
                    .Where(e => e.Type == EventType.Stop && 
                               e.Id != clockInPoint.Id && // Exclude clock-in point from filter
                               e.DurationMinutes >= minStayMinutes.Value)
                    .OrderBy(e => e.StartTime)
                    .ToList();

                if (validDestinationStays.Count == 0)
                {
                    _logger.LogInformation($"⚠️ No destination stays ≥{minStayMinutes}min found for FE #{engineerId}");
                    // Return only clock-in point if no valid destinations
                    return Ok(new List<ActivityEvent> { clockInPoint });
                }

                // 🚗 Build the result: Clock-in + Drives + Valid Destination Stays
                var result = new List<ActivityEvent> { clockInPoint };
                ActivityEvent? lastStay = clockInPoint;

                foreach (var targetStay in validDestinationStays)
                {
                    // Get drives between last stay and current target stay
                    var driveBetween = allEvents
                        .Where(e => e.Type == EventType.Drive &&
                                   e.StartTime >= (lastStay?.EndTime ?? DateTime.MinValue) &&
                                   e.EndTime <= targetStay.StartTime)
                        .OrderBy(e => e.StartTime)
                        .ToList();

                    result.AddRange(driveBetween);
                    result.Add(targetStay);
                    
                    lastStay = targetStay;
                }

                var finalResult = result
                    .OrderBy(e => e.StartTime)
                    .Distinct()
                    .ToList();

                _logger.LogInformation($"✅ Filtered to {finalResult.Count} events (stay ≥{minStayMinutes}min filter) for FE #{engineerId}");
                _logger.LogInformation($"   Clock-in: {clockInPoint.Address}, Valid destinations: {validDestinationStays.Count}");

                return Ok(finalResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error fetching activity history for FE #{engineerId}");
                return StatusCode(500, "Internal server error");
            }
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