using Google.Apis.Auth.OAuth2;
using System.Text.Json;
using System.Net.Http.Headers;

public class FirebaseMessagingService
{
    private readonly GoogleCredential _credential;

    public FirebaseMessagingService()
    {
        var json = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT");
        if (string.IsNullOrEmpty(json))
            throw new Exception("FIREBASE_SERVICE_ACCOUNT not set");

        _credential = GoogleCredential.FromJson(json)
            .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
    }

    private async Task<string> GetAccessTokenAsync()
    {
        return await _credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
    }

    public async Task SendNotificationAsync(string projectId, string fcmToken, string title, string body)
    {
        var accessToken = await GetAccessTokenAsync();
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

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

        var response = await http.PostAsync(
            $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send",
            content);

        Console.WriteLine($"📡 FCM Response: {response.StatusCode}");
    }
}
