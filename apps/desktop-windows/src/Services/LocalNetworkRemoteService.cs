using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Media.Imaging;
using DrawingImage = System.Drawing.Image;
using FormsScreen = System.Windows.Forms.Screen;

namespace SecureRemoteDesk.Desktop.Services;

public sealed record IncomingLocalRequest(string ViewerName, IPEndPoint RemoteEndPoint);

public sealed record IncomingFileOffer(string TransferId, string FileName, long SizeBytes, bool IsFromHost);
public sealed record IncomingFileDecision(string TransferId, bool Accepted);
public sealed record FileChunkPayload(string TransferId, int ChunkIndex, byte[] Data);

public sealed class LocalHostSessionServer : IAsyncDisposable
{
    private const byte MsgString = (byte)'S';
    private const byte MsgBinary = (byte)'B';

    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpListener? _listener;
    private TcpClient? _pendingClient;
    private NetworkStream? _pendingStream;

    public LocalHostSessionServer(int port = 50555)
    {
        _port = port;
    }

    public string ConnectAddress => $"{GetLocalIpAddress()}:{_port}";

    public event Action<IncomingLocalRequest>? IncomingRequest;
    public event Action<string>? StatusChanged;
    public event Action<string>? InputReceived;
    public event Action<IncomingFileOffer>? FileOfferReceived;
    public event Action<IncomingFileDecision>? FileDecisionReceived;
    public event Action<FileChunkPayload>? FileChunkReceived;
    public event Action<string>? FileCompleteReceived;
    public event Action<string>? ClipboardReceived;

    public Task StartAsync()
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
        StatusChanged?.Invoke($"Host dinliyor: {ConnectAddress}");
        return Task.CompletedTask;
    }

    public async Task ApproveAsync(bool remoteControlGranted, bool fileTransferGranted, bool clipboardGranted, CancellationToken cancellationToken)
    {
        if (_pendingStream is null)
            throw new InvalidOperationException("No pending viewer request.");

        var bounds = FormsScreen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1280, 720);
        await WriteRawStringAsync(_pendingStream, "APPROVED", cancellationToken);
        await WriteRawStringAsync(_pendingStream, $"SCREEN|{bounds.Width}|{bounds.Height}", cancellationToken);
        await WriteRawStringAsync(_pendingStream, $"CONTROL|{(remoteControlGranted ? "granted" : "denied")}", cancellationToken);
        await WriteRawStringAsync(_pendingStream, $"FILETRANSFER|{(fileTransferGranted ? "granted" : "denied")}", cancellationToken);
        await WriteRawStringAsync(_pendingStream, $"CLIPBOARD|{(clipboardGranted ? "granted" : "denied")}", cancellationToken);
        StatusChanged?.Invoke("Oturum onaylandı, ekran paylaşımı başladı.");
        _ = SendFramesAsync(_pendingStream, _cts.Token);
        _ = ReadMessageLoopAsync(_pendingStream, _cts.Token, fromHost: false);
    }

    public async Task SendFileOfferAsync(string transferId, string fileName, long sizeBytes, CancellationToken cancellationToken)
    {
        if (_pendingStream is null) return;
        await WriteTypedStringAsync(_pendingStream, $"FILE_OFFER|{transferId}|{fileName}|{sizeBytes}", cancellationToken);
    }

    public async Task SendFileDecisionAsync(string transferId, bool accepted, CancellationToken cancellationToken)
    {
        if (_pendingStream is null) return;
        var msg = accepted ? $"FILE_ACCEPT|{transferId}" : $"FILE_REJECT|{transferId}";
        await WriteTypedStringAsync(_pendingStream, msg, cancellationToken);
    }

    public async Task SendFileChunkAsync(string transferId, int chunkIndex, byte[] payload, CancellationToken cancellationToken)
    {
        if (_pendingStream is null) return;
        // Header + payload atomik yazılmalı, JPEG frame yazıcısı araya girmesin
        var header = System.Text.Encoding.UTF8.GetBytes($"FILE_CHUNK|{transferId}|{chunkIndex}");
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _pendingStream.WriteAsync(new[] { MsgString }, cancellationToken);
            await WriteBytesInternalAsync(_pendingStream, header, cancellationToken);
            await _pendingStream.WriteAsync(new[] { MsgBinary }, cancellationToken);
            await WriteBytesInternalAsync(_pendingStream, payload, cancellationToken);
            await _pendingStream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendFileCompleteAsync(string transferId, CancellationToken cancellationToken)
    {
        if (_pendingStream is null) return;
        await WriteTypedStringAsync(_pendingStream, $"FILE_COMPLETE|{transferId}", cancellationToken);
    }

    public async Task SendClipboardAsync(string text, CancellationToken cancellationToken)
    {
        if (_pendingStream is null) return;
        await WriteTypedStringAsync(_pendingStream, $"CLIPBOARD|{text}", cancellationToken);
    }

    private async Task ReadMessageLoopAsync(Stream stream, CancellationToken cancellationToken, bool fromHost)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var (type, data) = await ReadTypedAsync(stream, cancellationToken);
                if (type != 'S')
                    continue;

                var message = System.Text.Encoding.UTF8.GetString(data);
                if (message.StartsWith("FILE_OFFER|", StringComparison.Ordinal))
                {
                    var parts = message.Split('|', 4);
                    if (parts.Length == 4 && long.TryParse(parts[3], out var size))
                        FileOfferReceived?.Invoke(new IncomingFileOffer(parts[1], parts[2], size, IsFromHost: fromHost));
                }
                else if (message.StartsWith("FILE_ACCEPT|", StringComparison.Ordinal))
                {
                    FileDecisionReceived?.Invoke(new IncomingFileDecision(message["FILE_ACCEPT|".Length..], true));
                }
                else if (message.StartsWith("FILE_REJECT|", StringComparison.Ordinal))
                {
                    FileDecisionReceived?.Invoke(new IncomingFileDecision(message["FILE_REJECT|".Length..], false));
                }
                else if (message.StartsWith("FILE_CHUNK|", StringComparison.Ordinal))
                {
                    var parts = message.Split('|');
                    if (parts.Length == 3 && int.TryParse(parts[2], out var idx))
                    {
                        var (payloadType, payload) = await ReadTypedAsync(stream, cancellationToken);
                        if (payloadType == 'B')
                            FileChunkReceived?.Invoke(new FileChunkPayload(parts[1], idx, payload));
                    }
                }
                else if (message.StartsWith("FILE_COMPLETE|", StringComparison.Ordinal))
                {
                    FileCompleteReceived?.Invoke(message["FILE_COMPLETE|".Length..]);
                }
                else if (message.StartsWith("CLIPBOARD|", StringComparison.Ordinal))
                {
                    ClipboardReceived?.Invoke(message["CLIPBOARD|".Length..]);
                }
                else
                {
                    InputReceived?.Invoke(message);
                }
            }
        }
        catch (Exception)
        {
            // Stream closed when the session ends; nothing left to read.
        }
    }

    public async Task RejectAsync(CancellationToken cancellationToken)
    {
        if (_pendingStream is not null)
            await WriteRawStringAsync(_pendingStream, "REJECTED", cancellationToken);
        ClosePending();
        StatusChanged?.Invoke("Bağlantı isteği reddedildi.");
    }

    public async Task EndAsync()
    {
        if (_pendingStream is not null)
        {
            try
            {
                await WriteRawStringAsync(_pendingStream, "ENDED", CancellationToken.None);
            }
            catch
            {
                // Karşı taraf bağlantıyı zaten kapatmış olabilir
            }
        }
        ClosePending();
        StatusChanged?.Invoke("Oturum sonlandırıldı.");
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            var stream = client.GetStream();
            var message = await ReadRawStringAsync(stream, cancellationToken);
            if (!message.StartsWith("REQUEST|", StringComparison.Ordinal))
            {
                client.Dispose();
                continue;
            }

            ClosePending();
            _pendingClient = client;
            _pendingStream = stream;
            var viewerName = message["REQUEST|".Length..];
            IncomingRequest?.Invoke(new IncomingLocalRequest(viewerName, (IPEndPoint)client.Client.RemoteEndPoint!));
        }
    }

    private async Task SendFramesAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = CapturePrimaryScreenJpeg();
                await _writeLock.WaitAsync(cancellationToken);
                try
                {
                    await WriteTypedBytesUnlockedAsync(stream, frame, cancellationToken);
                }
                finally
                {
                    _writeLock.Release();
                }
                await Task.Delay(500, cancellationToken);
            }
        }
        catch (Exception)
        {
            // Session ended.
        }
    }

    private static byte[] CapturePrimaryScreenJpeg()
    {
        var bounds = FormsScreen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1280, 720);
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        }

        var maxWidth = 1280;
        using var output = bitmap.Width > maxWidth
            ? new Bitmap(bitmap, maxWidth, (int)(bitmap.Height * (maxWidth / (double)bitmap.Width)))
            : new Bitmap(bitmap);

        using var ms = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(x => x.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 55L);
        output.Save(ms, codec, parameters);
        return ms.ToArray();
    }

    private static string GetLocalIpAddress()
    {
        return Dns.GetHostEntry(Dns.GetHostName())
            .AddressList
            .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))
            ?.ToString() ?? "127.0.0.1";
    }

    private void ClosePending()
    {
        _pendingStream?.Dispose();
        _pendingClient?.Dispose();
        _pendingStream = null;
        _pendingClient = null;
    }

    public async ValueTask DisposeAsync()
    {
        try { await EndAsync(); } catch { }
        try { _cts.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _cts.Dispose();
    }

    // --- Framing helpers (raw, no type tag — used during handshake) ---

    internal static async Task WriteRawStringAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        await WriteBytesInternalAsync(stream, bytes, cancellationToken);
    }

    internal static async Task<string> ReadRawStringAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = await ReadBytesInternalAsync(stream, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    // --- Framing helpers (typed, used after handshake) ---

    internal async Task WriteTypedStringAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await WriteTypedStringUnlockedAsync(stream, value, bytes, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task WriteTypedStringUnlockedAsync(Stream stream, string value, byte[] bytes, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new[] { MsgString }, cancellationToken);
        await WriteBytesInternalAsync(stream, bytes, cancellationToken);
    }

    internal async Task WriteTypedBytesAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await WriteTypedBytesUnlockedAsync(stream, bytes, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task WriteTypedBytesUnlockedAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new[] { MsgBinary }, cancellationToken);
        await WriteBytesInternalAsync(stream, bytes, cancellationToken);
    }

    internal static async Task<(char Type, byte[] Data)> ReadTypedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var typeBuf = new byte[1];
        await stream.ReadExactlyAsync(typeBuf, cancellationToken);
        var data = await ReadBytesInternalAsync(stream, cancellationToken);
        return ((char)typeBuf[0], data);
    }

    private static async Task WriteBytesInternalAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        var length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadBytesInternalAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
        var length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes));
        if (length is <= 0 or > 100_000_000)
            throw new InvalidOperationException("Invalid frame size.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }
}

public sealed class LocalViewerSessionClient : IAsyncDisposable
{
    private const byte MsgString = (byte)'S';
    private const byte MsgBinary = (byte)'B';

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;

    public event Action<BitmapImage>? FrameReceived;
    public event Action<string>? StatusChanged;
    public event Action<IncomingFileOffer>? FileOfferReceived;
    public event Action<IncomingFileDecision>? FileDecisionReceived;
    public event Action<FileChunkPayload>? FileChunkReceived;
    public event Action<string>? FileCompleteReceived;
    public event Action<string>? ClipboardReceived;

    public int RemoteScreenWidth { get; private set; } = 1920;
    public int RemoteScreenHeight { get; private set; } = 1080;
    public bool RemoteControlGranted { get; private set; }
    public bool FileTransferGranted { get; private set; }
    public bool ClipboardGranted { get; private set; }

    public async Task ConnectAsync(string hostAndPort, CancellationToken cancellationToken)
    {
        var parts = hostAndPort.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            throw new InvalidOperationException("Bağlantı kodu IP:PORT formatında olmalı. Örnek: 192.168.1.10:50555");

        _client = new TcpClient();
        await _client.ConnectAsync(parts[0], port, cancellationToken);
        var stream = _client.GetStream();
        _stream = stream;
        await LocalHostSessionServer.WriteRawStringAsync(stream, $"REQUEST|{Environment.UserName}", cancellationToken);
        StatusChanged?.Invoke("Host onayı bekleniyor.");

        var decision = await LocalHostSessionServer.ReadRawStringAsync(stream, cancellationToken);
        if (decision != "APPROVED")
        {
            StatusChanged?.Invoke("Host bağlantıyı reddetti.");
            return;
        }

        var screenMessage = await LocalHostSessionServer.ReadRawStringAsync(stream, cancellationToken);
        var screenParts = screenMessage.Split('|');
        if (screenParts.Length == 3 && int.TryParse(screenParts[1], out var w) && int.TryParse(screenParts[2], out var h))
        {
            RemoteScreenWidth = w;
            RemoteScreenHeight = h;
        }

        var controlMessage = await LocalHostSessionServer.ReadRawStringAsync(stream, cancellationToken);
        RemoteControlGranted = controlMessage == "CONTROL|granted";

        var fileMessage = await LocalHostSessionServer.ReadRawStringAsync(stream, cancellationToken);
        FileTransferGranted = fileMessage == "FILETRANSFER|granted";

        var clipboardMessage = await LocalHostSessionServer.ReadRawStringAsync(stream, cancellationToken);
        ClipboardGranted = clipboardMessage == "CLIPBOARD|granted";

        StatusChanged?.Invoke(BuildStatusMessage());
        _ = ReadMessageLoopAsync(stream, _cts.Token);
    }

    private string BuildStatusMessage()
    {
        var control = RemoteControlGranted ? "açık" : "kapalı";
        var file = FileTransferGranted ? "açık" : "kapalı";
        var clip = ClipboardGranted ? "açık" : "kapalı";
        return $"Bağlandı. Kontrol: {control}, Dosya: {file}, Clipboard: {clip}.";
    }

    public async Task SendPointerMoveAsync(double normalizedX, double normalizedY)
    {
        await SendInputAsync($"MOVE|{normalizedX.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{normalizedY.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }

    public async Task SendPointerButtonAsync(bool left, bool down)
    {
        await SendInputAsync($"{(down ? "DOWN" : "UP")}|{(left ? "L" : "R")}");
    }

    public async Task SendWheelAsync(int delta)
    {
        await SendInputAsync($"WHEEL|{delta}");
    }

    public async Task SendKeyAsync(int virtualKey, bool down)
    {
        await SendInputAsync($"{(down ? "KEYDOWN" : "KEYUP")}|{virtualKey}");
    }

    public async Task SendFileOfferAsync(string transferId, string fileName, long sizeBytes, CancellationToken cancellationToken)
    {
        if (_stream is null) return;
        await WriteTypedStringAsync(_stream, $"FILE_OFFER|{transferId}|{fileName}|{sizeBytes}", cancellationToken);
    }

    public async Task SendFileDecisionAsync(string transferId, bool accepted, CancellationToken cancellationToken)
    {
        if (_stream is null) return;
        var msg = accepted ? $"FILE_ACCEPT|{transferId}" : $"FILE_REJECT|{transferId}";
        await WriteTypedStringAsync(_stream, msg, cancellationToken);
    }

    public async Task SendFileChunkAsync(string transferId, int chunkIndex, byte[] payload, CancellationToken cancellationToken)
    {
        if (_stream is null) return;
        // Header + payload atomik yazılmalı, frame yazıcısı araya girmesin
        var header = System.Text.Encoding.UTF8.GetBytes($"FILE_CHUNK|{transferId}|{chunkIndex}");
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _stream.WriteAsync(new[] { MsgString }, cancellationToken);
            await WriteBytesInternalAsync(_stream, header, cancellationToken);
            await _stream.WriteAsync(new[] { MsgBinary }, cancellationToken);
            await WriteBytesInternalAsync(_stream, payload, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendFileCompleteAsync(string transferId, CancellationToken cancellationToken)
    {
        if (_stream is null) return;
        await WriteTypedStringAsync(_stream, $"FILE_COMPLETE|{transferId}", cancellationToken);
    }

    public async Task SendClipboardAsync(string text, CancellationToken cancellationToken)
    {
        if (_stream is null) return;
        await WriteTypedStringAsync(_stream, $"CLIPBOARD|{text}", cancellationToken);
    }

    private async Task SendInputAsync(string message)
    {
        if (_stream is null || !RemoteControlGranted)
            return;

        var bytes = System.Text.Encoding.UTF8.GetBytes(message);
        await _writeLock.WaitAsync();
        try
        {
            await WriteTypedStringUnlockedAsync(_stream, bytes, CancellationToken.None);
        }
        catch (Exception)
        {
            // Best-effort: dropped input is safe to ignore, the session may be ending.
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadMessageLoopAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var (type, data) = await LocalHostSessionServer.ReadTypedAsync(stream, cancellationToken);
                if (type == 'B')
                {
                    FrameReceived?.Invoke(ToBitmapImage(data));
                }
                else if (type == 'S')
                {
                    var message = System.Text.Encoding.UTF8.GetString(data);
                    if (message.StartsWith("FILE_OFFER|", StringComparison.Ordinal))
                    {
                        var parts = message.Split('|', 4);
                        if (parts.Length == 4 && long.TryParse(parts[3], out var size))
                            FileOfferReceived?.Invoke(new IncomingFileOffer(parts[1], parts[2], size, IsFromHost: true));
                    }
                    else if (message.StartsWith("FILE_ACCEPT|", StringComparison.Ordinal))
                    {
                        FileDecisionReceived?.Invoke(new IncomingFileDecision(message["FILE_ACCEPT|".Length..], true));
                    }
                    else if (message.StartsWith("FILE_REJECT|", StringComparison.Ordinal))
                    {
                        FileDecisionReceived?.Invoke(new IncomingFileDecision(message["FILE_REJECT|".Length..], false));
                    }
                    else if (message.StartsWith("FILE_CHUNK|", StringComparison.Ordinal))
                    {
                        var parts = message.Split('|');
                        if (parts.Length == 3 && int.TryParse(parts[2], out var idx))
                        {
                            var (payloadType, payload) = await LocalHostSessionServer.ReadTypedAsync(stream, cancellationToken);
                            if (payloadType == 'B')
                                FileChunkReceived?.Invoke(new FileChunkPayload(parts[1], idx, payload));
                        }
                    }
                    else if (message.StartsWith("FILE_COMPLETE|", StringComparison.Ordinal))
                    {
                        FileCompleteReceived?.Invoke(message["FILE_COMPLETE|".Length..]);
                    }
                    else if (message.StartsWith("CLIPBOARD|", StringComparison.Ordinal))
                    {
                        ClipboardReceived?.Invoke(message["CLIPBOARD|".Length..]);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Stream closed when the session ends; nothing left to read.
        }
    }

    private static BitmapImage ToBitmapImage(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        try { _client?.Dispose(); } catch { }
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task WriteTypedStringAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(new[] { MsgString }, cancellationToken);
            await WriteBytesInternalAsync(stream, bytes, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task WriteTypedStringUnlockedAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new[] { MsgString }, cancellationToken);
        await WriteBytesInternalAsync(stream, bytes, cancellationToken);
    }

    private async Task WriteTypedBytesAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(new[] { MsgBinary }, cancellationToken);
            await WriteBytesInternalAsync(stream, bytes, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task WriteBytesInternalAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        var length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
