namespace SecureRemoteDesk.Domain.Entities;

public sealed class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public required string DeviceName { get; set; }
    public required string DeviceType { get; set; }
    public required string PublicDeviceId { get; set; }
    public required string FingerprintHash { get; set; }
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public string? AppVersion { get; set; }
    public bool IsRevoked { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public User? OwnerUser { get; set; }
}
