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
public async Task<ActionResult<ServiceRequest>> PostServiceRequest(ServiceRequest serviceRequest)
{
    // Check if the branch already exists
    var existingBranch = await _context.Branches.FindAsync(serviceRequest.BranchId);
    if (existingBranch == null)
    {
        return NotFound($"Branch with ID {serviceRequest.BranchId} not found.");
    }

    // Detach any branch entity that might have come with the request
    if (serviceRequest.Branch != null && _context.Entry(serviceRequest.Branch).State != EntityState.Detached)
    {
        _context.Entry(serviceRequest.Branch).State = EntityState.Detached;
    }

    // Use the existing branch reference
    serviceRequest.Branch = existingBranch;
    
    // Set any missing required fields
    if (string.IsNullOrEmpty(serviceRequest.Status))
    {
        serviceRequest.Status = "pending";
    }
    
    if (serviceRequest.CreatedAt == default)
    {
        serviceRequest.CreatedAt = DateTime.UtcNow;
    }
    
    if (serviceRequest.UpdatedAt == default)
    {
        serviceRequest.UpdatedAt = DateTime.UtcNow;
    }
    
    // Set ID to 0 to ensure it's treated as a new entity
    serviceRequest.Id = 0;

    _context.ServiceRequests.Add(serviceRequest);
    await _context.SaveChangesAsync();

    // Send notification about the new service request
    await _notificationService.SendNewServiceRequestNotification(serviceRequest);

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