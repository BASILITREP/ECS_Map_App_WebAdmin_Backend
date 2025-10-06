
using Microsoft.AspNetCore.Mvc;
using EcsFeMappingApi.Services;
using EcsFeMappingApi.Models;

namespace EcsFeMappingApi.Controllers
{
    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }


    public class LoginResponse
    {
        public string Token { get; set; }
        public string Username { get; set; }
        public string Role { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

        // In Controllers/AuthController.cs
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            var user = await _authService.ValidateUser(loginDto.Username, loginDto.Password);
            if (user == null)
            {
                return Unauthorized("Invalid credentials.");
            }

            var token = _authService.GenerateJwtToken(user);

            // THIS IS THE KEY: Set the token in a secure httpOnly cookie
            Response.Cookies.Append("jwt-token", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true, // Send only over HTTPS
                SameSite = SameSiteMode.Strict, // Best CSRF protection
                Expires = DateTime.UtcNow.AddHours(1)
            });

            // Return user info, but NOT the token
            return Ok(new { username = user.Username, role = user.Role });
        }
    }
    public class LoginDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
