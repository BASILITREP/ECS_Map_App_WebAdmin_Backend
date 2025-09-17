
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
        
        [HttpPost("login")]
        public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
        {
            var user = await _authService.ValidateUser(request.Username, request.Password);
            
            if (user == null)
                return Unauthorized(new { message = "Invalid username or password" });
                
            var token = _authService.GenerateJwtToken(user);
            
            return Ok(new LoginResponse
            {
                Token = token,
                Username = user.Username,
                Role = user.Role
            });
        }
    }
}