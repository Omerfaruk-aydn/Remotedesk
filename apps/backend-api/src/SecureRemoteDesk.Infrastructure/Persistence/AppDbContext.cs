using Microsoft.EntityFrameworkCore;
using SecureRemoteDesk.Application.Interfaces;
using SecureRemoteDesk.Domain.Entities;

namespace SecureRemoteDesk.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PairingCode> PairingCodes => Set<PairingCode>();
    public DbSet<RemoteSession> Sessions => Set<RemoteSession>();
    public DbSet<SessionMetric> SessionMetrics => Set<SessionMetric>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.DisplayName).HasMaxLength(160);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<Device>(e =>
        {
            e.HasIndex(x => x.PublicDeviceId).IsUnique();
            e.HasIndex(x => new { x.OwnerUserId, x.DeviceName });
            e.Property(x => x.DeviceName).HasMaxLength(160);
            e.Property(x => x.DeviceType).HasMaxLength(40);
        });

        modelBuilder.Entity<PairingCode>(e =>
        {
            e.HasIndex(x => new { x.DeviceId, x.ExpiresAt });
            e.Property(x => x.CodeHash).HasMaxLength(160);
        });

        modelBuilder.Entity<RemoteSession>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => new { x.HostDeviceId, x.Status });
            e.HasIndex(x => new { x.ViewerUserId, x.RequestedAt });
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(80);
            e.HasIndex(x => new { x.Action, x.CreatedAt });
        });

        modelBuilder.Entity<SecurityEvent>(e =>
        {
            e.Property(x => x.Severity).HasMaxLength(24);
            e.Property(x => x.EventType).HasMaxLength(80);
            e.HasIndex(x => new { x.EventType, x.CreatedAt });
        });
    }
}
