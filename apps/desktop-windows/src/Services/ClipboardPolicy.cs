namespace SecureRemoteDesk.Desktop.Services;

public sealed class ClipboardPolicy
{
    private Guid? _approvedSessionId;

    public bool IsAllowed { get; private set; }

    public void Enable(Guid approvedSessionId, bool clipboardApproved)
    {
        _approvedSessionId = approvedSessionId;
        IsAllowed = clipboardApproved;
    }

    public void Disable()
    {
        _approvedSessionId = null;
        IsAllowed = false;
    }

    public void EnsureCanShare(Guid sessionId)
    {
        if (!IsAllowed || _approvedSessionId != sessionId)
            throw new InvalidOperationException("Clipboard sharing requires an approved active session.");
    }
}
