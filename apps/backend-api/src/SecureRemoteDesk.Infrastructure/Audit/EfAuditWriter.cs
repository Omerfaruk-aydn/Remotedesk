using SecureRemoteDesk.Application.Interfaces;
using SecureRemoteDesk.Domain.Entities;
using SecureRemoteDesk.Infrastructure.Persistence;

namespace SecureRemoteDesk.Infrastructure.Audit;

public sealed class EfAuditWriter : IAuditWriter
{
    private readonly AppDbContext _db;

    public EfAuditWriter(AppDbContext db) => _db = db;

    public async Task WriteAsync(string action, string resourceType, string? resourceId, Guid? actorUserId, Guid? actorDeviceId, CancellationToken cancellationToken)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ActorUserId = actorUserId,
            ActorDeviceId = actorDeviceId
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
