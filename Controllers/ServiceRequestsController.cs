using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcsFeMappingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ServiceRequestsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ServiceRequestsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/ServiceRequests
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ServiceRequestDto>>> GetServiceRequests()
        {
            var list = await _context.ServiceRequests
                .Include(sr => sr.Branch)
                .Include(sr => sr.FieldEngineer)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var dtos = list.Select(sr => ServiceRequestDto.FromEntity(sr, now)).ToList();
            return Ok(dtos);
        }

        // GET: api/ServiceRequests/5
        [HttpGet("{id}")]
    public async Task<ActionResult<ServiceRequestDto>> GetServiceRequest(int id)
        {
            var serviceRequest = await _context.ServiceRequests
                .Include(sr => sr.Branch)
                .Include(sr => sr.FieldEngineer)
                .FirstOrDefaultAsync(sr => sr.Id == id);

            if (serviceRequest == null)
            {
                return NotFound();
            }

            return ServiceRequestDto.FromEntity(serviceRequest, DateTime.UtcNow);
        }

        // POST: api/ServiceRequests
        [HttpPost]
        public async Task<ActionResult<ServiceRequest>> CreateServiceRequest(ServiceRequest serviceRequest)
        {
            // Ensure the branch exists
            var branch = await _context.Branches.FindAsync(serviceRequest.BranchId);
            if (branch == null)
            {
                return BadRequest("Branch not found");
            }

            // Set timestamps
            serviceRequest.CreatedAt = DateTime.UtcNow;
            serviceRequest.UpdatedAt = DateTime.UtcNow;
            
            // Default status to pending if not provided
            if (string.IsNullOrEmpty(serviceRequest.Status))
            {
                serviceRequest.Status = "pending";
            }

            _context.ServiceRequests.Add(serviceRequest);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetServiceRequest), new { id = serviceRequest.Id }, ServiceRequestDto.FromEntity(serviceRequest, DateTime.UtcNow));
        }

        // POST: api/ServiceRequests/5/accept
        [HttpPost("{id}/accept")]
        public async Task<IActionResult> AcceptServiceRequest(int id, [FromBody] AcceptRequestDto dto)
        {
            var serviceRequest = await _context.ServiceRequests.FindAsync(id);
            if (serviceRequest == null)
            {
                return NotFound("Service request not found");
            }

            if (serviceRequest.Status != "pending")
            {
                return BadRequest("Service request is not in a pending state");
            }

            var fieldEngineer = await _context.FieldEngineers.FindAsync(dto.FieldEngineerId);
            if (fieldEngineer == null)
            {
                return NotFound("Field Engineer not found");
            }

            // Update service request
            serviceRequest.Status = "accepted";
            serviceRequest.FieldEngineerId = dto.FieldEngineerId;
            serviceRequest.UpdatedAt = DateTime.UtcNow;

            // Update field engineer status
            fieldEngineer.IsAvailable = false;
            fieldEngineer.Status = "On Assignment";
            fieldEngineer.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Service request accepted successfully" });
        }

        // POST: api/ServiceRequests/{id}/auto-assign
        [HttpPost("{id}/auto-assign")]
        public async Task<IActionResult> AutoAssign(int id)
        {
            var sr = await _context.ServiceRequests
                .Include(s => s.Branch)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (sr == null) return NotFound("Service request not found");
            if (sr.Status != "pending") return BadRequest("Service request is not pending");
            if (sr.Branch == null) return BadRequest("Branch missing");

            var candidates = await _context.FieldEngineers
                .Where(fe => fe.IsAvailable && fe.CurrentLatitude != null && fe.CurrentLongitude != null)
                .ToListAsync();
            if (!candidates.Any()) return BadRequest("No available field engineers");

            // choose nearest by haversine
            double toRad(double deg) => deg * Math.PI / 180.0;
            double hav(double lat1, double lon1, double lat2, double lon2)
            {
                const double R = 6371.0;
                var dLat = toRad(lat2 - lat1);
                var dLon = toRad(lon2 - lon1);
                var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                        Math.Cos(toRad(lat1)) * Math.Cos(toRad(lat2)) *
                        Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
                var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
                return R * c;
            }

            var branchLat = sr.Branch.Latitude;
            var branchLng = sr.Branch.Longitude;
            var nearest = candidates
                .Select(fe => new
                {
                    FE = fe,
                    Km = hav(fe.CurrentLatitude!.Value, fe.CurrentLongitude!.Value, branchLat, branchLng)
                })
                .OrderBy(x => x.Km)
                .First();

            // assign
            sr.Status = "accepted";
            sr.FieldEngineerId = nearest.FE.Id;
            sr.UpdatedAt = DateTime.UtcNow;
            nearest.FE.IsAvailable = false;
            nearest.FE.Status = "On Assignment";
            nearest.FE.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // return DTO including radius for consistency
            return Ok(ServiceRequestDto.FromEntity(sr, DateTime.UtcNow));
        }

        // PUT: api/ServiceRequests/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateServiceRequest(int id, ServiceRequest serviceRequest)
        {
            if (id != serviceRequest.Id)
            {
                return BadRequest();
            }

            serviceRequest.UpdatedAt = DateTime.UtcNow;
            _context.Entry(serviceRequest).State = EntityState.Modified;
            _context.Entry(serviceRequest).Property(x => x.CreatedAt).IsModified = false;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ServiceRequestExists(id))
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

        // DELETE: api/ServiceRequests/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteServiceRequest(int id)
        {
            var serviceRequest = await _context.ServiceRequests.FindAsync(id);
            if (serviceRequest == null)
            {
                return NotFound();
            }

            _context.ServiceRequests.Remove(serviceRequest);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ServiceRequestExists(int id)
        {
            return _context.ServiceRequests.Any(e => e.Id == id);
        }
    }

    public class AcceptRequestDto
    {
        public int FieldEngineerId { get; set; }
    }

    public class ServiceRequestDto
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? FieldEngineerId { get; set; }
        public string? FieldEngineerName { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lng { get; set; }
        public double CurrentRadiusKm { get; set; }

        public static ServiceRequestDto FromEntity(EcsFeMappingApi.Models.ServiceRequest sr, DateTime now)
        {
            // Simulate expanding radius: start at 3km, grow 1km per minute up to 20km
            var minutes = Math.Max(0, (now - sr.CreatedAt).TotalMinutes);
            var radius = Math.Min(20.0, 3.0 + minutes * 1.0);

            return new ServiceRequestDto
            {
                Id = sr.Id,
                BranchId = sr.BranchId,
                Title = sr.Title,
                Description = sr.Description,
                Status = sr.Status,
                Priority = sr.Priority,
                CreatedAt = sr.CreatedAt,
                UpdatedAt = sr.UpdatedAt,
                FieldEngineerId = sr.FieldEngineerId,
                FieldEngineerName = sr.FieldEngineer?.Name,
                BranchName = sr.Branch?.Name ?? string.Empty,
                Lat = sr.Branch?.Latitude ?? 0,
                Lng = sr.Branch?.Longitude ?? 0,
                CurrentRadiusKm = radius
            };
        }
    }
}
