namespace SecureRemoteDesk.Desktop.Services;

public sealed class GuardedRemoteControlPolicy
{
    private Guid? _approvedSessionId;

    public bool IsRemoteControlEnabled { get; private set; }

    public void Enable(Guid approvedSessionId, bool remoteControlApproved)
    {
        _approvedSessionId = approvedSessionId;
        IsRemoteControlEnabled = remoteControlApproved;
    }

    public void Disable()
    {
        _approvedSessionId = null;
        IsRemoteControlEnabled = false;
    }

    public void EnsureCanApplyInput(Guid sessionId)
    {
        if (!IsRemoteControlEnabled || _approvedSessionId != sessionId)
            throw new InvalidOperationException("Remote input requires an approved active session.");
    }
}
