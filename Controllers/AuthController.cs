using DraculaVanHelsing.Api.DTOs.Requests;
using DraculaVanHelsing.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DraculaVanHelsing.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var response = await _authService.RegisterAsync(request);
            if (response == null)
            {
                return BadRequest(new { message = "Username hoặc Email đã tồn tại!" });
            }

            return Ok(response);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var response = await _authService.LoginAsync(request);
            if (response == null)
            {
                return Unauthorized(new { message = "Sai Username hoặc Mật khẩu!" });
            }

            return Ok(response);
        }
    }
}