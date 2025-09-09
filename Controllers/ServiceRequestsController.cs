using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using EcsFeMappingApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace EcsFeMappingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ServiceRequestsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly NotificationService _notificationService;

        public ServiceRequestsController(AppDbContext context, NotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        // GET: api/ServiceRequests
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ServiceRequest>>> GetServiceRequests()
        {
            return await _context.ServiceRequests
                .Include(sr => sr.Branch)
                .Include(sr => sr.FieldEngineer)
                .ToListAsync();
        }

        // GET: api/ServiceRequests/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ServiceRequest>> GetServiceRequest(int id)
        {
            var serviceRequestModel = await _context.ServiceRequests
                .Include(sr => sr.Branch)
                .Include(sr => sr.FieldEngineer)
                .FirstOrDefaultAsync(sr => sr.Id == id);

            if (serviceRequestModel == null)
            {
                return NotFound();
            }

            return serviceRequestModel;
        }

        // POST: api/ServiceRequests
        [HttpPost]
        public async Task<ActionResult<ServiceRequest>> PostServiceRequest(ServiceRequestCreateDto srDto)
        {
            var branch = await _context.Branches.FindAsync(srDto.BranchId);
            if (branch == null)
            {
                return BadRequest("Invalid BranchId");
            }

            var serviceRequest = new ServiceRequest
            {
                BranchId = srDto.BranchId,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                Lat = branch.Latitude,
                Lng = branch.Longitude,
                BranchName = branch.Name
            };

            _context.ServiceRequests.Add(serviceRequest);
            await _context.SaveChangesAsync();

            // Broadcast the new service request via SignalR
            await _notificationService.SendNewServiceRequest(serviceRequest);

            return CreatedAtAction("GetServiceRequest", new { id = serviceRequest.Id }, serviceRequest);
        }

        // POST: api/ServiceRequests/5/accept
        [HttpPost("{id}/accept")]
        public async Task<IActionResult> AcceptServiceRequest(int id, [FromBody] AcceptRequest payload)
        {
            var serviceRequest = await _context.ServiceRequests.FindAsync(id);
            if (serviceRequest == null)
            {
                return NotFound("Service request not found.");
            }

            if (serviceRequest.Status != "pending")
            {
                return BadRequest("Service request is not pending.");
            }

            var fieldEngineer = await _context.FieldEngineers.FindAsync(payload.FieldEngineerId);
            if (fieldEngineer == null)
            {
                return NotFound("Field engineer not found.");
            }

            serviceRequest.Status = "accepted";
            serviceRequest.AcceptedAt = DateTime.UtcNow;
            serviceRequest.FieldEngineerId = payload.FieldEngineerId;

            await _context.SaveChangesAsync();

            // Broadcast the update via SignalR
            await _notificationService.SendServiceRequestUpdate(serviceRequest);

            return Ok();
        }

        private bool ServiceRequestModelExists(int id)
        {
            return _context.ServiceRequests.Any(e => e.Id == id);
        }

        public class AcceptRequest
        {
            public int FieldEngineerId { get; set; }
        }
    }
}