namespace SecureRemoteDesk.Host.Services;

public sealed record IncomingConnectionRequest(Guid SessionId, string ViewerName, string ViewerEmail, bool RemoteControlRequested);
public sealed record DisplayInfo(string Id, string Name, int Width, int Height);
public sealed record CapturedFrame(byte[] Bytes, int Width, int Height, DateTimeOffset CapturedAt);

public interface IScreenCaptureService
{
    IAsyncEnumerable<CapturedFrame> CaptureAsync(DisplayInfo display, CancellationToken cancellationToken);
}

public interface IWebRtcStreamingService
{
    Task StartAsync(Guid sessionId, IAsyncEnumerable<CapturedFrame> frames, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IInputInjectionService
{
    bool IsEnabled { get; }
    void Enable(Guid approvedSessionId, bool remoteControlAllowed);
    void Disable();
    Task ApplyMouseMoveAsync(Guid sessionId, double x, double y, CancellationToken cancellationToken);
}

public sealed class MockScreenCaptureService : IScreenCaptureService
{
    public async IAsyncEnumerable<CapturedFrame> CaptureAsync(DisplayInfo display, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return new CapturedFrame(Array.Empty<byte>(), display.Width, display.Height, DateTimeOffset.UtcNow);
            await Task.Delay(250, cancellationToken);
        }
    }
}

public sealed class GuardedInputInjectionService : IInputInjectionService
{
    private Guid? _approvedSessionId;
    public bool IsEnabled { get; private set; }

    public void Enable(Guid approvedSessionId, bool remoteControlAllowed)
    {
        _approvedSessionId = approvedSessionId;
        IsEnabled = remoteControlAllowed;
    }

    public void Disable()
    {
        _approvedSessionId = null;
        IsEnabled = false;
    }

    public Task ApplyMouseMoveAsync(Guid sessionId, double x, double y, CancellationToken cancellationToken)
    {
        if (!IsEnabled || _approvedSessionId != sessionId)
            throw new InvalidOperationException("Remote input is disabled or session is not approved.");
        if (x is < 0 or > 1 || y is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(x), "Coordinates must be normalized.");
        return Task.CompletedTask;
    }
}
