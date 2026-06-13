namespace SecureRemoteDesk.Viewer.Services;

public sealed record SessionMetrics(int LatencyMs, int Fps, int BitrateKbps, decimal PacketLoss);

public interface IWebRtcViewerService
{
    Task ConnectAsync(Guid sessionId, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}

public interface IRemoteInputService
{
    bool CanSendInput { get; }
    void Enable(Guid sessionId, bool remoteControlApproved);
    void Disable();
    Task SendMouseMoveAsync(double x, double y, CancellationToken cancellationToken);
}

public sealed class RemoteInputService : IRemoteInputService
{
    private Guid? _sessionId;
    public bool CanSendInput { get; private set; }

    public void Enable(Guid sessionId, bool remoteControlApproved)
    {
        _sessionId = sessionId;
        CanSendInput = remoteControlApproved;
    }

    public void Disable()
    {
        _sessionId = null;
        CanSendInput = false;
    }

    public Task SendMouseMoveAsync(double x, double y, CancellationToken cancellationToken)
    {
        if (!CanSendInput || _sessionId is null)
            throw new InvalidOperationException("Remote control permission is not active.");
        return Task.CompletedTask;
    }
}
