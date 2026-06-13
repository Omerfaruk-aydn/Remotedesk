namespace SecureRemoteDesk.Application.Contracts;

public sealed record SessionPermissions(bool ScreenView = true, bool RemoteControl = false, bool FileTransfer = false, bool Clipboard = false, bool Audio = false);
public sealed record SessionRequestDto(Guid HostDeviceId, Guid ViewerDeviceId, SessionPermissions RequestedPermissions);
public sealed record SessionDto(Guid Id, Guid HostDeviceId, Guid ViewerDeviceId, string Status, SessionPermissions RequestedPermissions, SessionPermissions? ApprovedPermissions);
public sealed record SessionDecisionRequest(SessionPermissions ApprovedPermissions);
public sealed record EndSessionRequest(string Reason);
