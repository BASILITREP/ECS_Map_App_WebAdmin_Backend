using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class FcmNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly string _projectId;
    private readonly IConfiguration _configuration;

    public FcmNotificationService(IConfiguration configuration)
    {
        _configuration = configuration;
        _projectId = _configuration["Firebase:ProjectId"]; // Add this to appsettings.json
        _projectId = configuration["Firebase:ProjectId"]; // Add this to appsettings.json
    }

    public async Task SendNotificationAsync(string fcmToken, string title, string body, Dictionary<string, string>? data = null)
{
    var message = new
    {
        message = new
        {
            token = fcmToken,
            notification = new
            {
                title = title,
                body = body
            },
            data = data ?? new Dictionary<string, string>()
        }
    };

    var jsonMessage = JsonSerializer.Serialize(message);
    var serviceAccountJson = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT");
if (string.IsNullOrEmpty(serviceAccountJson))
{
    throw new Exception("Firebase service account JSON is not configured.");
}

var credential = GoogleCredential.FromJson(serviceAccountJson)
    .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");

    var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();

    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var content = new StringContent(jsonMessage, Encoding.UTF8, "application/json");
    var response = await _httpClient.PostAsync($"https://fcm.googleapis.com/v1/projects/{_projectId}/messages:send", content);

    if (!response.IsSuccessStatusCode)
    {
        var error = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Failed to send FCM notif: {error}"); // Add logging
        throw new Exception($"Failed to send FCM notification: {error}");
    }
    else
    {
        Console.WriteLine($"Notification sent successfully to FCM token: {fcmToken}"); // Add logging
    }
}
}