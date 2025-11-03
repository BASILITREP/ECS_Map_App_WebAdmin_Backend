using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EcsFeMappingApi.Services
{
    public class SmsService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmsService> _logger;
        private readonly HttpClient _httpClient;

        public SmsService(IConfiguration config, ILogger<SmsService> logger)
        {
            _config = config;
            _logger = logger;
            _httpClient = new HttpClient();
        }

        public async Task SendAsync(string phoneNumber, string message)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                _logger.LogWarning("⚠️ SMS not sent — no phone number provided.");
                return;
            }

            try
            {
                string apiKey = _config["Semaphore:ApiKey"];
                string sender = _config["Semaphore:SenderName"] ?? "DOROTHY";

                var payload = new
                {
                    apikey = apiKey,
                    number = phoneNumber,
                    message,
                    sendername = sender
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("https://api.semaphore.co/api/v4/messages", content);

                if (response.IsSuccessStatusCode)
                    _logger.LogInformation($"📩 SMS sent successfully to {phoneNumber}");
                else
                    _logger.LogWarning($"❌ SMS send failed for {phoneNumber}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"🔥 SMS error for {phoneNumber}: {ex.Message}");
            }
        }
    }
}
