namespace SecureRemoteDesk.Desktop.Services;

public sealed class FileTransferPolicy
{
    private Guid? _approvedSessionId;

    public bool IsAllowed { get; private set; }

    public void Enable(Guid approvedSessionId, bool fileTransferApproved)
    {
        _approvedSessionId = approvedSessionId;
        IsAllowed = fileTransferApproved;
    }

    public void Disable()
    {
        _approvedSessionId = null;
        IsAllowed = false;
    }

    public void EnsureCanTransfer(Guid sessionId)
    {
        if (!IsAllowed || _approvedSessionId != sessionId)
            throw new InvalidOperationException("File transfer requires an approved active session.");
    }
}
