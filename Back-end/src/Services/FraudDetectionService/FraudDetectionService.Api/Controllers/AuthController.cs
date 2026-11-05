using FraudDetectionService.Application.Features.Auth.Commands;
using System.ComponentModel.DataAnnotations;
using FraudDetectionService.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace FraudDetectionService.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(LoginCommandHandler login) : ControllerBase
{
    [HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        var result = await login.Handle(new(req.Username, req.Password), ct);
        return result == null ? Unauthorized(new
        {
            error = "Tên đăng nhập hoặc mật khẩu không đúng."
        }) : Ok(result);
    }
    [HttpGet("me"), Authorize] public object Me() => new { username = User.Identity!.Name, role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value };
}
public record LoginRequest([Required, StringLength(80)] string Username, [Required, StringLength(200)] string Password);
