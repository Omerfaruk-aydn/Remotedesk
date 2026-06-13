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

public sealed class LocalHostSessionServer : IAsyncDisposable
{
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
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

    public Task StartAsync()
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
        StatusChanged?.Invoke($"Host dinliyor: {ConnectAddress}");
        return Task.CompletedTask;
    }

    public async Task ApproveAsync(CancellationToken cancellationToken)
    {
        if (_pendingStream is null)
            throw new InvalidOperationException("No pending viewer request.");

        await WriteStringAsync(_pendingStream, "APPROVED", cancellationToken);
        StatusChanged?.Invoke("Oturum onaylandı, ekran paylaşımı başladı.");
        _ = SendFramesAsync(_pendingStream, _cts.Token);
    }

    public async Task RejectAsync(CancellationToken cancellationToken)
    {
        if (_pendingStream is not null)
            await WriteStringAsync(_pendingStream, "REJECTED", cancellationToken);
        ClosePending();
        StatusChanged?.Invoke("Bağlantı isteği reddedildi.");
    }

    public async Task EndAsync()
    {
        if (_pendingStream is not null)
            await WriteStringAsync(_pendingStream, "ENDED", CancellationToken.None);
        ClosePending();
        StatusChanged?.Invoke("Oturum sonlandırıldı.");
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            var stream = client.GetStream();
            var message = await ReadStringAsync(stream, cancellationToken);
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

    private static async Task SendFramesAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = CapturePrimaryScreenJpeg();
            await WriteBytesAsync(stream, frame, cancellationToken);
            await Task.Delay(500, cancellationToken);
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
        await EndAsync();
        _cts.Cancel();
        _listener?.Stop();
        _cts.Dispose();
    }

    internal static async Task WriteStringAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        await WriteBytesAsync(stream, bytes, cancellationToken);
    }

    internal static async Task<string> ReadStringAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = await ReadBytesAsync(stream, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    internal static async Task WriteBytesAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        var length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal static async Task<byte[]> ReadBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
        var length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes));
        if (length is <= 0 or > 5_000_000)
            throw new InvalidOperationException("Invalid frame size.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }
}

public sealed class LocalViewerSessionClient : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private TcpClient? _client;

    public event Action<BitmapImage>? FrameReceived;
    public event Action<string>? StatusChanged;

    public async Task ConnectAsync(string hostAndPort, CancellationToken cancellationToken)
    {
        var parts = hostAndPort.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            throw new InvalidOperationException("Bağlantı kodu IP:PORT formatında olmalı. Örnek: 192.168.1.10:50555");

        _client = new TcpClient();
        await _client.ConnectAsync(parts[0], port, cancellationToken);
        var stream = _client.GetStream();
        await LocalHostSessionServer.WriteStringAsync(stream, $"REQUEST|{Environment.UserName}", cancellationToken);
        StatusChanged?.Invoke("Host onayı bekleniyor.");

        var decision = await LocalHostSessionServer.ReadStringAsync(stream, cancellationToken);
        if (decision != "APPROVED")
        {
            StatusChanged?.Invoke("Host bağlantıyı reddetti.");
            return;
        }

        StatusChanged?.Invoke("Bağlandı. Ekran görüntüsü alınıyor.");
        _ = ReceiveFramesAsync(stream, _cts.Token);
    }

    private async Task ReceiveFramesAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await LocalHostSessionServer.ReadBytesAsync(stream, cancellationToken);
            FrameReceived?.Invoke(ToBitmapImage(frame));
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
        _cts.Cancel();
        _client?.Dispose();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
