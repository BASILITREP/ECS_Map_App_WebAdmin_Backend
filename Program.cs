using EcsFeMappingApi.Data;
using Microsoft.EntityFrameworkCore;
using EcsFeMappingApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "wwwroot"
});

// Configure Kestrel for Railway deployment
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    // Railway uses the PORT environment variable
    var port = Environment.GetEnvironmentVariable("PORT");
    if (!string.IsNullOrEmpty(port))
    {
        serverOptions.ListenAnyIP(int.Parse(port));
    }
    else
    {
        serverOptions.ListenAnyIP(5242); // Fallback for local development
    }
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();

builder.Services.AddScoped<EcsFeMappingApi.Services.NotificationService>();
builder.Services.AddScoped<EcsFeMappingApi.Services.AuthService>();
builder.Services.AddScoped<FcmNotificationService>();
builder.Services.AddSingleton<ActivityProcessingService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ActivityProcessingService>());

// Configure MySQL
var connectionString = Environment.GetEnvironmentVariable("MYSQL_URL") 
                      ?? builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// CORS configuration - FIXED
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:3000",
                "http://localhost:5242",
                "https://localhost:7126",
                "http://192.168.211.42",
                "https://sdstestwebservices.equicom.com",
                "https://ecsmapappwebadminbackend-production.up.railway.app" // Add your own domain
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();

    });
});

// Configure Redis
// Configure Redis (Railway-compatible)
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisUrl = Environment.GetEnvironmentVariable("REDIS_URL");

    if (string.IsNullOrEmpty(redisUrl))
    {
        Console.WriteLine("⚠️ REDIS_URL not found. Redis caching disabled.");
        // Return a fake in-memory connection to prevent DI crash
        return ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
    }

    try
    {
        var uri = new Uri(redisUrl);
        var userInfoParts = uri.UserInfo.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var password = userInfoParts.Length > 1 ? userInfoParts[1] : string.Empty;
        var configString = $"{uri.Host}:{uri.Port},password={password},abortConnect=false,connectRetry=3,connectTimeout=5000";

        Console.WriteLine($"✅ Redis connecting to {uri.Host}:{uri.Port}...");
        var mux = ConnectionMultiplexer.Connect(configString);

        Console.WriteLine($"✅ Redis connected successfully!");
        return mux;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Redis connection failed: {ex.Message}");
        // Still register a fallback connection to prevent DI failure
        return ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
    }
});


// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "YourTemporaryFallbackSecretKeyForDevelopmentOnly";
var key = Encoding.ASCII.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Important for Railway
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true
    };
});

var app = builder.Build();

// Auto-create database tables on startup
using (var scope = app.Services.CreateScope())
{
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync();
        Console.WriteLine("✅ Railway database tables created successfully!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Database creation error: {ex.Message}");
    }
}

// Configure the HTTP request pipeline.
// Always enable Swagger for Railway (so your boss can see API docs)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ECS FE Mapping API V1");
    c.RoutePrefix = "swagger"; // Access via /swagger
});

app.UseRouting();

// Important: Use CORS before other middleware
app.UseCors("AllowFrontend");

app.UseWebSockets();

// Add authentication middleware
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Map SignalR hub
app.MapHub<NotificationHub>("/notificationHub");

// Add a simple health check endpoint
app.MapGet("/", () => new
{
    message = "ECS FE Mapping API is running!",
    timestamp = DateTime.UtcNow,
    environment = app.Environment.EnvironmentName,
    database = "Railway MySQL"
});

app.MapPost("/trigger-activity", async (ActivityProcessingService svc) =>
{
    await svc.TriggerProcessingAsync();
    return Results.Ok(new { message = "Manual activity processing triggered!" });
});


// Test endpoint for CORS
app.MapGet("/api/test", () => new {
    message = "CORS test endpoint working!",
    timestamp = DateTime.UtcNow,
    cors = "Success"
}).RequireCors("AllowFrontend");

app.Run();