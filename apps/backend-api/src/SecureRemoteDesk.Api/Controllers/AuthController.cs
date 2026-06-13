using Microsoft.AspNetCore.Mvc;
using SecureRemoteDesk.Application.Contracts;
using SecureRemoteDesk.Application.Services;

namespace SecureRemoteDesk.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth) => _auth = auth;

    [HttpPost("register")]
    public Task<AuthResponse> Register(RegisterRequest request, CancellationToken cancellationToken) => _auth.RegisterAsync(request, cancellationToken);

    [HttpPost("login")]
    public Task<AuthResponse> Login(LoginRequest request, CancellationToken cancellationToken) => _auth.LoginAsync(request, cancellationToken);
}
