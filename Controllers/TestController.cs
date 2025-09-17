using EcsFeMappingApi.Models;
using EcsFeMappingApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace EcsFeMappingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        private readonly IHubContext<NotificationHub> _hubContext;
        private static readonly Dictionary<int, CancellationTokenSource> _navigationSessions = new();

        public TestController(IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        [HttpPost("startNavigation")]
        public async Task<IActionResult> StartFieldEngineerNavigation([FromBody] NavigationRequest request)
        {
            try
            {
                // Cancel any existing navigation for this FE
                if (_navigationSessions.ContainsKey(request.FieldEngineerId))
                {
                    _navigationSessions[request.FieldEngineerId].Cancel();
                    _navigationSessions.Remove(request.FieldEngineerId);
                }

                // Create new cancellation token
                var cts = new CancellationTokenSource();
                _navigationSessions[request.FieldEngineerId] = cts;

                // Start navigation in background
                _ = Task.Run(async () => await SimulateNavigation(request, cts.Token), cts.Token);

                return Ok($"Navigation started for FE {request.FieldEngineerId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting navigation: {ex.Message}");
                return BadRequest($"Error starting navigation: {ex.Message}");
            }
        }

        [HttpPost("stopNavigation/{fieldEngineerId}")]
        public IActionResult StopFieldEngineerNavigation(int fieldEngineerId)
        {
            if (_navigationSessions.ContainsKey(fieldEngineerId))
            {
                _navigationSessions[fieldEngineerId].Cancel();
                _navigationSessions.Remove(fieldEngineerId);
                return Ok($"Navigation stopped for FE {fieldEngineerId}");
            }
            return NotFound($"No active navigation found for FE {fieldEngineerId}");
        }

        private async Task SimulateNavigation(NavigationRequest request, CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine($"Starting navigation for FE {request.FieldEngineerId} with {request.RouteCoordinates.Count} coordinates");

                // Create the field engineer object
                var fieldEngineer = new FieldEngineer
                {
                    Id = request.FieldEngineerId,
                    Name = request.FieldEngineerName,
                    CurrentLatitude = request.RouteCoordinates[0][1], // lat
                    CurrentLongitude = request.RouteCoordinates[0][0], // lng
                    Status = "On Assignment",
                    UpdatedAt = DateTime.Now
                };

                // Navigate through each coordinate in the polyline
                for (int i = 1; i < request.RouteCoordinates.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine($"Navigation cancelled for FE {request.FieldEngineerId}");
                        break;
                    }

                    var currentCoord = request.RouteCoordinates[i - 1];
                    var nextCoord = request.RouteCoordinates[i];

                    // Calculate intermediate steps between coordinates for smoother movement
                    var steps = CalculateIntermediateSteps(currentCoord, nextCoord, 5); // 5 steps between each coordinate

                    foreach (var step in steps)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        // Update field engineer position
                        fieldEngineer.CurrentLatitude = step[1]; // lat
                        fieldEngineer.CurrentLongitude = step[0]; // lng
                        fieldEngineer.UpdatedAt = DateTime.Now;

                        // Send update to all clients
                        await _hubContext.Clients.All.SendAsync("ReceiveNewFieldEngineer", fieldEngineer, cancellationToken);

                        Console.WriteLine($"FE {request.FieldEngineerId} moved to: {step[1]}, {step[0]}");

                        // Wait before next movement (adjust speed here)
                        await Task.Delay(1200, cancellationToken); // 1200ms between updates for smoother movement
                    }
                }

                // Mark as arrived when navigation completes
                if (!cancellationToken.IsCancellationRequested)
                {
                    fieldEngineer.Status = "Active"; // Back to active status
                    await _hubContext.Clients.All.SendAsync("ReceiveNewFieldEngineer", fieldEngineer, cancellationToken);
                    Console.WriteLine($"FE {request.FieldEngineerId} arrived at destination");
                }

                // Clean up the session
                _navigationSessions.Remove(request.FieldEngineerId);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Navigation was cancelled for FE {request.FieldEngineerId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during navigation for FE {request.FieldEngineerId}: {ex.Message}");
            }
        }

        private List<double[]> CalculateIntermediateSteps(double[] start, double[] end, int stepCount)
        {
            var steps = new List<double[]>();
            
            for (int i = 1; i <= stepCount; i++)
            {
                var ratio = (double)i / stepCount;
                var lng = start[0] + (end[0] - start[0]) * ratio;
                var lat = start[1] + (end[1] - start[1]) * ratio;
                steps.Add(new double[] { lng, lat });
            }
            
            return steps;
        }

        [HttpGet("sendTestFE")]
        public async Task<IActionResult> SendTestFieldEngineer()
        {
            var testFE = new FieldEngineer
    {
        Id = 999,
        Name = "GGWP",
        CurrentLatitude = 14.904217,
        CurrentLongitude = 120.457504,
        Status = "Active",
        UpdatedAt = DateTime.Now
    };
    
    Console.WriteLine($"Starting movement simulation for FE {testFE.Name}");

    // Simulate movement by updating coordinates periodically
            for (int i = 0; i < 20; i++) 
            {

                testFE.CurrentLatitude += 0.0100; 
                testFE.CurrentLongitude += 0.0100; 
                testFE.UpdatedAt = DateTime.Now;

                Console.WriteLine($"Sending update {i + 1}: Lat={testFE.CurrentLatitude}, Lng={testFE.CurrentLongitude}");


                await _hubContext.Clients.All.SendAsync("ReceiveNewFieldEngineer", testFE);

               
                await Task.Delay(1000);
            }

            Console.WriteLine("Movement simulation completed");

    return Ok("Test field engineer movement simulation completed");
        }
    }

    // Add this model class
    public class NavigationRequest
    {
        public int FieldEngineerId { get; set; }
        public string FieldEngineerName { get; set; }
        public List<double[]> RouteCoordinates { get; set; } = new();
    }
}