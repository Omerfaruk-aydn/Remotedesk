namespace SecureRemoteDesk.Application.Contracts;

public sealed record DeviceRegisterRequest(string DeviceName, string DeviceType, string Fingerprint, string? OsName, string? OsVersion, string? AppVersion);
public sealed record DeviceDto(Guid Id, string PublicDeviceId, string DeviceName, string DeviceType, bool IsRevoked, DateTimeOffset? LastSeenAt);
public sealed record PairingCodeResponse(string Code, DateTimeOffset ExpiresAt);
public sealed record PairDeviceRequest(string Code, Guid ViewerDeviceId);
