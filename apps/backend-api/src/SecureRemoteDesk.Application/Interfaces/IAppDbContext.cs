using Microsoft.EntityFrameworkCore;
using SecureRemoteDesk.Domain.Entities;

namespace SecureRemoteDesk.Application.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Device> Devices { get; }
    DbSet<PairingCode> PairingCodes { get; }
    DbSet<RemoteSession> Sessions { get; }
    DbSet<SessionMetric> SessionMetrics { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SecurityEvent> SecurityEvents { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
