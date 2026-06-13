using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureRemoteDesk.Application.Contracts;
using SecureRemoteDesk.Application.Interfaces;
using SecureRemoteDesk.Domain.Entities;
using SecureRemoteDesk.Domain.Enums;

namespace SecureRemoteDesk.Application.Services;

public sealed class SessionService
{
    private readonly IAppDbContext _db;
    private readonly IAuditWriter _audit;

    public SessionService(IAppDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<SessionDto> RequestAsync(Guid viewerUserId, SessionRequestDto request, CancellationToken cancellationToken)
    {
        var hostDevice = await _db.Devices.SingleAsync(x => x.Id == request.HostDeviceId && !x.IsRevoked, cancellationToken);
        var session = new RemoteSession
        {
            HostDeviceId = request.HostDeviceId,
            ViewerDeviceId = request.ViewerDeviceId,
            HostUserId = hostDevice.OwnerUserId,
            ViewerUserId = viewerUserId,
            RequestedPermissionsJson = JsonSerializer.Serialize(request.RequestedPermissions)
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(AuditAction.SessionRequested, "Session", session.Id.ToString(), viewerUserId, request.ViewerDeviceId, cancellationToken);
        return ToDto(session);
    }

    public async Task<SessionDto> ApproveAsync(Guid hostUserId, Guid sessionId, SessionDecisionRequest request, CancellationToken cancellationToken)
    {
        var session = await _db.Sessions.SingleAsync(x => x.Id == sessionId && x.HostUserId == hostUserId, cancellationToken);
        session.Status = SessionStatus.Approved;
        session.ApprovedAt = DateTimeOffset.UtcNow;
        session.ApprovedPermissionsJson = JsonSerializer.Serialize(request.ApprovedPermissions with { ScreenView = true });
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(AuditAction.SessionApproved, "Session", session.Id.ToString(), hostUserId, session.HostDeviceId, cancellationToken);
        return ToDto(session);
    }

    public async Task<SessionDto> EndAsync(Guid userId, Guid sessionId, EndSessionRequest request, CancellationToken cancellationToken)
    {
        var session = await _db.Sessions.SingleAsync(x => x.Id == sessionId && (x.HostUserId == userId || x.ViewerUserId == userId), cancellationToken);
        session.Status = SessionStatus.Ended;
        session.EndedAt = DateTimeOffset.UtcNow;
        session.EndReason = request.Reason;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(AuditAction.SessionEnded, "Session", session.Id.ToString(), userId, null, cancellationToken);
        return ToDto(session);
    }

    private static SessionDto ToDto(RemoteSession session) => new(
        session.Id,
        session.HostDeviceId,
        session.ViewerDeviceId,
        session.Status.ToString(),
        JsonSerializer.Deserialize<SessionPermissions>(session.RequestedPermissionsJson) ?? new SessionPermissions(),
        session.ApprovedPermissionsJson is null ? null : JsonSerializer.Deserialize<SessionPermissions>(session.ApprovedPermissionsJson));
}
