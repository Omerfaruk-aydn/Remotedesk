using SecureRemoteDesk.Domain.Enums;

namespace SecureRemoteDesk.Domain.Entities;

public sealed class RemoteSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HostDeviceId { get; set; }
    public Guid ViewerDeviceId { get; set; }
    public Guid HostUserId { get; set; }
    public Guid ViewerUserId { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Requested;
    public required string RequestedPermissionsJson { get; set; }
    public string? ApprovedPermissionsJson { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? EndReason { get; set; }
    public string? ViewerIpHash { get; set; }
    public string? HostIpHash { get; set; }
}
