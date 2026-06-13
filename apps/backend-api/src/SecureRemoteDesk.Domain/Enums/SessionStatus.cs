namespace SecureRemoteDesk.Domain.Enums;

public enum SessionStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2,
    Active = 3,
    Ended = 4,
    TimedOut = 5
}
