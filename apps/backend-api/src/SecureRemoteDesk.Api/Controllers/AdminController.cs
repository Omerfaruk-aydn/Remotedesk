using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureRemoteDesk.Domain.Enums;
using SecureRemoteDesk.Infrastructure.Persistence;

namespace SecureRemoteDesk.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminController(AppDbContext db) => _db = db;

    [HttpGet("overview")]
    public async Task<object> Overview(CancellationToken cancellationToken)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        return new
        {
            totalUsers = await _db.Users.CountAsync(cancellationToken),
            onlineDevices = await _db.Devices.CountAsync(x => x.LastSeenAt != null && x.LastSeenAt > DateTimeOffset.UtcNow.AddMinutes(-2), cancellationToken),
            activeSessions = await _db.Sessions.CountAsync(x => x.Status == SessionStatus.Active, cancellationToken),
            securityEventsLast24h = await _db.SecurityEvents.CountAsync(x => x.CreatedAt >= since, cancellationToken)
        };
    }
}
