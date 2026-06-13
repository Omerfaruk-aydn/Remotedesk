using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureRemoteDesk.Application.Contracts;
using SecureRemoteDesk.Application.Services;

namespace SecureRemoteDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly SessionService _sessions;

    public SessionsController(SessionService sessions) => _sessions = sessions;

    [HttpPost("request")]
    public Task<SessionDto> Request(SessionRequestDto request, CancellationToken cancellationToken) =>
        _sessions.RequestAsync(UserId, request, cancellationToken);

    [HttpPost("{sessionId:guid}/approve")]
    public Task<SessionDto> Approve(Guid sessionId, SessionDecisionRequest request, CancellationToken cancellationToken) =>
        _sessions.ApproveAsync(UserId, sessionId, request, cancellationToken);

    [HttpPost("{sessionId:guid}/end")]
    public Task<SessionDto> End(Guid sessionId, EndSessionRequest request, CancellationToken cancellationToken) =>
        _sessions.EndAsync(UserId, sessionId, request, cancellationToken);

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
