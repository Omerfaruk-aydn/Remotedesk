namespace SecureRemoteDesk.Domain.Enums;

public static class AuditAction
{
    public const string UserRegistered = "USER_REGISTERED";
    public const string UserLoginSuccess = "USER_LOGIN_SUCCESS";
    public const string UserLoginFailed = "USER_LOGIN_FAILED";
    public const string DeviceRegistered = "DEVICE_REGISTERED";
    public const string DeviceRevoked = "DEVICE_REVOKED";
    public const string PairingCodeCreated = "PAIRING_CODE_CREATED";
    public const string PairingCodeFailed = "PAIRING_CODE_FAILED";
    public const string SessionRequested = "SESSION_REQUESTED";
    public const string SessionApproved = "SESSION_APPROVED";
    public const string SessionRejected = "SESSION_REJECTED";
    public const string SessionStarted = "SESSION_STARTED";
    public const string SessionEnded = "SESSION_ENDED";
}
