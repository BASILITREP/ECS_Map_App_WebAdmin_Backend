using System.Threading.Tasks;
using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;
using EcsFeMappingApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;


namespace EcsFeMappingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ServiceRequestsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly NotificationService _notificationService;
        private readonly IConfiguration _configuration;

        public ServiceRequestsController(AppDbContext context, NotificationService notificationService, IConfiguration configuration)
        {
            _context = context;
            _notificationService = notificationService;
            _configuration = configuration;
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
    try
    {
        // Check if the branch already exists
        var existingBranch = await _context.Branches.FindAsync(serviceRequest.BranchId);
        if (existingBranch != null)
        {
            // Associate the existing branch with the service request
            serviceRequest.Branch = existingBranch;
        }
        else
        {
            // If no branch exists, add the new branch
            _context.Branches.Add(serviceRequest.Branch);
        }

        // Save the service request to the database
        _context.ServiceRequests.Add(serviceRequest);
        await _context.SaveChangesAsync();

        // Send notification to field engineers
        var fieldEngineers = await _context.FieldEngineers
            .Where(fe => !string.IsNullOrEmpty(fe.FcmToken)) // Use FcmToken instead of OneSignalPlayerId
            .ToListAsync();

        var fcmService = new FcmNotificationService(_configuration);
        foreach (var engineer in fieldEngineers)
        {
            Console.WriteLine($"Sending notification to Field Engineer: {engineer.Name}, FCM Token: {engineer.FcmToken}");
            await fcmService.SendNotificationAsync(
                        engineer.FcmToken, // Use FcmToken here
                        "New Service Request",
                        $"A new service request has been created for {serviceRequest.Branch?.Name ?? "Unknown location"}",
                        new Dictionary<string, string>
                        {
                    { "type", "new_service_request" },
                    { "serviceRequestId", serviceRequest.Id.ToString() },
                    { "branchName", serviceRequest.Branch?.Name ?? "Unknown" },
                    { "branchId", serviceRequest.BranchId.ToString() }
                        }
                    );
        }

        return CreatedAtAction("GetServiceRequest", new { id = serviceRequest.Id }, serviceRequest);
    }
    catch (Exception ex)
    {
        return BadRequest(new { error = ex.Message });
    }
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

        [HttpGet("woop")]
        public async Task<IActionResult> Woop()
        {
            try
            {
                var serviceRequests = await _context.ServiceRequests.Where(x => x.FieldEngineerId == null).ToListAsync();
                return Ok(serviceRequests);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        [HttpPost("{serviceId}/{id}")]
        public async Task<IActionResult> Test(int serviceId, int id)
        {
            var serviceRequest = await _context.ServiceRequests.FindAsync(serviceId);
            if (serviceRequest == null)
            {
                return NotFound("Service request not found.");
            }
            else
            {
                serviceRequest.FieldEngineerId = id;
                return Ok(await _context.SaveChangesAsync());
            }
        }
        
        
    }
            }
