using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly FcmNotificationService _fcmNotificationService; // Assuming you have this service

        public ServiceRequestsController(AppDbContext context, NotificationService notificationService, FcmNotificationService fcmNotificationService)
        {
            _context = context;
            _notificationService = notificationService;
            _fcmNotificationService = fcmNotificationService;
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
            var existingBranch = await _context.Branches.FindAsync(serviceRequest.BranchId);
            if (existingBranch == null)
            {
                return NotFound($"Branch with ID {serviceRequest.BranchId} not found.");
            }

            if (serviceRequest.Branch != null && _context.Entry(serviceRequest.Branch).State != EntityState.Detached)
            {
                _context.Entry(serviceRequest.Branch).State = EntityState.Detached;
            }

            serviceRequest.Branch = existingBranch;

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

            serviceRequest.Id = 0;

            _context.ServiceRequests.Add(serviceRequest);
            await _context.SaveChangesAsync();

            await _notificationService.SendNewServiceRequestNotification(serviceRequest);

            // --- New Notification Logic ---
            await NotifyFieldEngineers(serviceRequest);
            // --------------------------

            return CreatedAtAction("GetServiceRequest", new { id = serviceRequest.Id }, serviceRequest);
        }

        // POST: api/ServiceRequests/5/accept
        [HttpPost("{id}/accept")]
public async Task<IActionResult> AcceptServiceRequest(int id, [FromBody] AcceptRequest payload)
{
    var serviceRequest = await _context.ServiceRequests.FindAsync(id);
    if (serviceRequest == null)
    {
        return NotFound();
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
    serviceRequest.FieldEngineerId = payload.FieldEngineerId;
    serviceRequest.UpdatedAt = DateTime.UtcNow;
    

    await _context.SaveChangesAsync();

    // Emit SignalR event
    await _notificationService.SendServiceRequestUpdate(serviceRequest);

    return Ok(serviceRequest);
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

        // --- Helper Method for Notifications ---
        private async Task NotifyFieldEngineers(ServiceRequest sr)
{
    const double radiusKm = 10.0;

    var allEngineers = await _context.FieldEngineers
                                     .Where(fe => fe.Status != "Inactive" && !string.IsNullOrEmpty(fe.FcmToken))
                                     .ToListAsync();

    var engineersInRange = allEngineers.Where(fe =>
        CalculateDistance(sr.Lat, sr.Lng, fe.CurrentLatitude, fe.CurrentLongitude) <= radiusKm
    ).ToList();

    if (engineersInRange.Any())
    {
        var tokens = engineersInRange.Select(fe => fe.FcmToken).ToList();
        var title = "New Service Request";
        var body = $"A new service request is available at {sr.BranchName}.";

        // Use the new method to send to multiple devices
        await _fcmNotificationService.SendNotificationToMultipleDevicesAsync(tokens, title, body);
    }
}

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371; // Radius of the Earth in kilometers
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private double ToRadians(double angle)
        {
            return Math.PI * angle / 180.0;
        }
        // ------------------------------------
    }
}