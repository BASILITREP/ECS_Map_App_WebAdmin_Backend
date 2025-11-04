using Google.Apis.Auth.OAuth2;
using System.Text.Json;
using System.Net.Http.Headers;

public class FirebaseMessagingService
{
    private readonly GoogleCredential _credential;
    private readonly HttpClient _httpClient;

    public FirebaseMessagingService()
    {
        Console.WriteLine("🚀 Initializing FirebaseMessagingService...");

        var json = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT");

        if (string.IsNullOrEmpty(json))
        {
            Console.WriteLine("❌ FIREBASE_SERVICE_ACCOUNT is not set or empty.");
            throw new Exception("FIREBASE_SERVICE_ACCOUNT environment variable not found.");
        }

        try
        {
            Console.WriteLine("✅ FIREBASE_SERVICE_ACCOUNT found. Initializing credentials...");

            _credential = GoogleCredential.FromJson(json)
                .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");

            _httpClient = new HttpClient();
            Console.WriteLine("✅ Firebase credentials initialized successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to initialize FirebaseMessagingService: {ex.Message}");
            throw;
        }
    }

    private async Task<string> GetAccessTokenAsync()
    {
        try
        {
            var token = await _credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
            return token ?? throw new Exception("Failed to retrieve Firebase access token.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error getting Firebase access token: {ex.Message}");
            throw;
        }
    }

    public async Task SendNotificationAsync(string projectId, string fcmToken, string title, string body)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var message = new
            {
                message = new
                {
                    token = fcmToken,
                    notification = new
                    {
                        title = title,
                        body = body
                    }
                }
            };

            var json = JsonSerializer.Serialize(message);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            Console.WriteLine($"📤 Sending FCM notification → {fcmToken.Substring(0, 10)}...");
            var response = await _httpClient.PostAsync(
                $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send",
                content
            );

            var result = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"📡 FCM Response: {response.StatusCode} → {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FCM Send Error: {ex.Message}");
        }
    }
}
