namespace SecureRemoteDesk.Domain.Entities;

public sealed class SecurityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Severity { get; set; }
    public required string EventType { get; set; }
    public Guid? UserId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? SessionId { get; set; }
    public required string Description { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
