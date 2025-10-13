using EcsFeMappingApi.Data;
using Microsoft.EntityFrameworkCore;
using EcsFeMappingApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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
builder.Services.AddHostedService<ActivityProcessingService>();

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
            .AllowCredentials()
            .SetIsOriginAllowed(origin => 
            {
                // Allow localhost in any environment for testing
                if (string.IsNullOrEmpty(origin)) return false;
                if (origin.StartsWith("http://localhost") || origin.StartsWith("https://localhost")) return true;
                if (origin.StartsWith("http://127.0.0.1") || origin.StartsWith("https://127.0.0.1")) return true;
                return true; // For production testing - remove this later for security
            });
    });
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

// Test endpoint for CORS
app.MapGet("/api/test", () => new {
    message = "CORS test endpoint working!",
    timestamp = DateTime.UtcNow,
    cors = "Success"
}).RequireCors("AllowFrontend");

app.Run();