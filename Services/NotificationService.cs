using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EcsFeMappingApi.Models;
using Microsoft.Extensions.Configuration;

namespace EcsFeMappingApi.Services
{
    public class NotificationService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _oneSignalAppId;
        private readonly string? _oneSignalApiKey;

       // Make sure the constructor looks something like this:
public NotificationService(IConfiguration configuration)
{
    _httpClient = new HttpClient();
    _oneSignalAppId = configuration["OneSignal:AppId"];
    _oneSignalApiKey = configuration["OneSignal:ApiKey"];
    if (!string.IsNullOrEmpty(_oneSignalApiKey))
    {
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {_oneSignalApiKey}");
    }
}

        public async Task SendNotificationToEngineers(string title, string message, List<FieldEngineer> engineers, object? data = null)
        {
            var playerIds = new List<string>();
            foreach (var fe in engineers)
            {
                if (!string.IsNullOrEmpty(fe.OneSignalPlayerId))
                    playerIds.Add(fe.OneSignalPlayerId);
            }
            if (playerIds.Count == 0) return;

            if (string.IsNullOrEmpty(_oneSignalAppId) || string.IsNullOrEmpty(_oneSignalApiKey))
            {
                Console.WriteLine("OneSignal AppId or ApiKey is not configured.");
                return;
            }

            var notification = new
            {
                app_id = _oneSignalAppId,
                headings = new { en = title },
                contents = new { en = message },
                include_player_ids = playerIds,
                data = data
            };

            var json = JsonSerializer.Serialize(notification);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://onesignal.com/api/v1/notifications", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                // Log error
                Console.WriteLine($"OneSignal API error: {responseContent}");
            }


        }
        public async Task SendNewServiceRequest(ServiceRequest serviceRequest)
        {
            // This method can be expanded to include more complex logic if needed
            // For now, it simply sends the new service request notification
            await SendNotificationToEngineers(
                "New Service Request",
                $"A new service request has been created for {serviceRequest.BranchName}",
                new List<FieldEngineer>(), // You would typically filter and pass relevant engineers here
                new { serviceRequestId = serviceRequest.Id }
            );
        }

        public async Task SendServiceRequestUpdate(ServiceRequest serviceRequest, FieldEngineer? fieldEngineer = null)
{
    if (serviceRequest.FieldEngineer != null && fieldEngineer == null)
    {
        fieldEngineer = serviceRequest.FieldEngineer;
    }
    
    if (fieldEngineer == null) return;

    await SendNotificationToEngineers(
        "Service Request Updated",
        $"Service request for {serviceRequest.BranchName} has been updated.",
        new List<FieldEngineer> { fieldEngineer },
        new { serviceRequestId = serviceRequest.Id }
    );
}
    }
}
