using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureRemoteDesk.Desktop.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfApplication = System.Windows.Application;

namespace SecureRemoteDesk.Desktop.ViewModels;

public partial class LocalFileEntry : ObservableObject
{
    [ObservableProperty] private string filePath = "";
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private long sizeBytes;

    public string SizeText => FormatSize(SizeBytes);

    public LocalFileEntry() { }

    public LocalFileEntry(string path)
    {
        FilePath = path;
        DisplayName = System.IO.Path.GetFileName(path);
        try { SizeBytes = new FileInfo(path).Length; } catch { SizeBytes = 0; }
        OnPropertyChanged(nameof(SizeText));
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes / (1024.0 * 1024.0):0.0} MB";
    }
}

public partial class TransferQueueItem : ObservableObject
{
    [ObservableProperty] private string transferId = "";
    [ObservableProperty] private string fileName = "";
    [ObservableProperty] private long sizeBytes;
    [ObservableProperty] private string direction = "";
    [ObservableProperty] private string status = "Bekliyor";
    [ObservableProperty] private double progress;
    [ObservableProperty] private string? localPath;

    public string SizeText => LocalFileEntry.FormatSize(SizeBytes);
    public string ProgressText => $"{Progress * 100:0}%";

    public TransferQueueItem() { }

    public TransferQueueItem(string transferId, string fileName, long sizeBytes, string direction)
    {
        TransferId = transferId;
        FileName = fileName;
        SizeBytes = sizeBytes;
        Direction = direction;
    }
}

public partial class IncomingOfferItem : ObservableObject
{
    [ObservableProperty] private string transferId = "";
    [ObservableProperty] private string fileName = "";
    [ObservableProperty] private long sizeBytes;
    [ObservableProperty] private bool isFromHost;
    [ObservableProperty] private string status = "Beklemede";

    public string SizeText => LocalFileEntry.FormatSize(SizeBytes);
    public string FromText => IsFromHost ? "Host'tan" : "Viewer'dan";

    public IncomingOfferItem() { }

    public IncomingOfferItem(string transferId, string fileName, long sizeBytes, bool isFromHost)
    {
        TransferId = transferId;
        FileName = fileName;
        SizeBytes = sizeBytes;
        IsFromHost = isFromHost;
    }
}

public partial class ShellViewModel : ObservableObject
{
    private static readonly MediaBrush ActiveNav = new SolidColorBrush(MediaColor.FromRgb(24, 69, 108));
    private static readonly MediaBrush InactiveNav = MediaBrushes.Transparent;
    private static readonly MediaBrush Green = new SolidColorBrush(MediaColor.FromRgb(34, 197, 94));
    private static readonly MediaBrush Gray = new SolidColorBrush(MediaColor.FromRgb(107, 114, 128));

    private const int FileChunkSize = 64 * 1024;

    private readonly GuardedRemoteControlPolicy _controlPolicy = new();
    private readonly FileTransferPolicy _fileTransferPolicy = new();
    private readonly ClipboardPolicy _clipboardPolicy = new();
    private readonly LocalHostSessionServer _host = new();
    private readonly LocalViewerSessionClient _viewer = new();
    private bool _hostStarted;
    private Guid? _sessionId;

    private readonly Dictionary<string, FileStream> _incomingFiles = new();

    [ObservableProperty] private string pairingCode = "Hazırlanıyor";
    [ObservableProperty] private string connectCode = "";
    [ObservableProperty] private string incomingRequestText = "Viewer modunda bağlantı kodu girildiğinde istek burada görünecek.";
    [ObservableProperty] private string hostStatusTitle = "Bağlantı isteği bekleniyor";
    [ObservableProperty] private string viewerSurfaceText = "Uzak ekran burada gösterilecek";
    [ObservableProperty] private string metricsText = "Son bağlantı isteği zaman aşımına uğradı veya reddedildi.";
    [ObservableProperty] private string topSessionText = "Aktif oturum: Yok";
    [ObservableProperty] private MediaBrush sessionDotBrush = Gray;
    [ObservableProperty] private BitmapImage? remoteFrame;
    [ObservableProperty] private bool grantRemoteControl = true;
    [ObservableProperty] private bool grantFileTransfer = false;
    [ObservableProperty] private bool grantClipboard = false;
    [ObservableProperty] private string remoteControlStatusText = "Kapalı";
    [ObservableProperty] private string fileTransferStatusText = "Kapalı";
    [ObservableProperty] private string clipboardStatusText = "Kapalı";
    [ObservableProperty] private string clipboardPreviewText = "Henüz pano içeriği paylaşılmadı.";

    [ObservableProperty] private Visibility hostVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility viewerVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility remoteVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility sessionsVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility devicesVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility fileTransferVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility securityVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility settingsVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility incomingModalVisibility = Visibility.Collapsed;

    [ObservableProperty] private MediaBrush hostNavBrush = ActiveNav;
    [ObservableProperty] private MediaBrush viewerNavBrush = InactiveNav;
    [ObservableProperty] private MediaBrush devicesNavBrush = InactiveNav;
    [ObservableProperty] private MediaBrush sessionsNavBrush = InactiveNav;
    [ObservableProperty] private MediaBrush fileTransferNavBrush = InactiveNav;
    [ObservableProperty] private MediaBrush securityNavBrush = InactiveNav;
    [ObservableProperty] private MediaBrush settingsNavBrush = InactiveNav;

    public ObservableCollection<LocalFileEntry> LocalFiles { get; } = new();
    public ObservableCollection<TransferQueueItem> TransferQueue { get; } = new();
    public ObservableCollection<IncomingOfferItem> IncomingOffers { get; } = new();
    public ObservableCollection<string> SecurityEvents { get; } = new()
    {
        "14:28  Info     REMOTE_CONTROL_DISABLED     Host uzaktan kontrol iznini kapattı",
        "14:30  Info     SESSION_REQUESTED          SUPPORT-LAPTOP bağlantı isteği gönderdi",
        "14:31  Warning  PAIRING_CODE_FAILED        Geçersiz bağlantı kodu denemesi"
    };

    public ShellViewModel()
    {
        _host.IncomingRequest += request => WpfApplication.Current.Dispatcher.Invoke(() =>
        {
            IncomingRequestText = $"{request.ViewerName} ({request.RemoteEndPoint.Address}) bu cihaza bağlanmak istiyor.";
            HostStatusTitle = "Onay bekleyen bağlantı isteği";
            GrantRemoteControl = true;
            GrantFileTransfer = false;
            GrantClipboard = false;
            IncomingModalVisibility = Visibility.Visible;
            SecurityEvents.Insert(0, "14:32  Info     SESSION_REQUESTED          Host tarafına bağlantı isteği geldi");
            ShowHost();
        });

        _host.StatusChanged += message => WpfApplication.Current.Dispatcher.Invoke(() =>
        {
            HostStatusTitle = message;
            SecurityEvents.Insert(0, $"14:32  Info     HOST_STATUS                {message}");
        });

        _host.InputReceived += ApplyRemoteInput;

        _host.FileOfferReceived += offer => WpfApplication.Current.Dispatcher.Invoke(() => HandleIncomingOffer(offer, fromHostSide: true));
        _host.FileDecisionReceived += decision => WpfApplication.Current.Dispatcher.Invoke(() => HandleFileDecision(decision, fromHostSide: true));
        _host.FileChunkReceived += chunk => WpfApplication.Current.Dispatcher.Invoke(() => HandleIncomingChunk(chunk, fromHostSide: true));
        _host.FileCompleteReceived += id => WpfApplication.Current.Dispatcher.Invoke(() => HandleFileComplete(id, fromHostSide: true));
        _host.ClipboardReceived += text => WpfApplication.Current.Dispatcher.Invoke(() => HandleClipboardReceived(text, fromHostSide: true));

        _viewer.StatusChanged += message => WpfApplication.Current.Dispatcher.Invoke(() =>
        {
            ViewerSurfaceText = message;
            MetricsText = $"Durum: {message} | FPS: 12 | Gecikme: yerel ağ";
            SecurityEvents.Insert(0, $"14:32  Info     VIEWER_STATUS              {message}");
        });

        _viewer.FrameReceived += frame => WpfApplication.Current.Dispatcher.Invoke(() =>
        {
            RemoteFrame = frame;
            ViewerSurfaceText = "";
            TopSessionText = "Aktif oturum: Bağlı";
            SessionDotBrush = Green;
            ShowRemote();
        });

        _viewer.FileOfferReceived += offer => WpfApplication.Current.Dispatcher.Invoke(() => HandleIncomingOffer(offer, fromHostSide: false));
        _viewer.FileDecisionReceived += decision => WpfApplication.Current.Dispatcher.Invoke(() => HandleFileDecision(decision, fromHostSide: false));
        _viewer.FileChunkReceived += chunk => WpfApplication.Current.Dispatcher.Invoke(() => HandleIncomingChunk(chunk, fromHostSide: false));
        _viewer.FileCompleteReceived += id => WpfApplication.Current.Dispatcher.Invoke(() => HandleFileComplete(id, fromHostSide: false));
        _viewer.ClipboardReceived += text => WpfApplication.Current.Dispatcher.Invoke(() => HandleClipboardReceived(text, fromHostSide: false));

        _ = StartHostIfNeededAsync();
        _ = ApplyUiTestIfConfiguredAsync();
    }

    public sealed class UiTestConfig
    {
        public string Mode { get; set; } = "";
        public string? ConnectCode { get; set; }
        public string[]? SendFiles { get; set; }
        public bool AutoApprove { get; set; }
        public bool AllowFileTransfer { get; set; }
        public bool AllowClipboard { get; set; }
        public string? DebugLogPath { get; set; }
    }

    public static UiTestConfig? UiTest { get; set; }

    private async Task ApplyUiTestIfConfiguredAsync()
    {
        var cfg = UiTest;
        if (cfg is null) return;
        void Log(string msg) => App.UiTestLog(cfg, msg);
        Log($"ApplyUiTestIfConfiguredAsync: Mode={cfg.Mode} AutoApprove={cfg.AutoApprove} AllowFile={cfg.AllowFileTransfer} AllowClip={cfg.AllowClipboard}");

        if (cfg.Mode == "host" && cfg.AutoApprove)
        {
            _host.IncomingRequest += req =>
            {
                Log($"IncomingRequest fired (background): viewer={req.ViewerName}");
                _ = WpfApplication.Current.Dispatcher.InvokeAsync(async () =>
                {
                    Log($"IncomingRequest UI thread entered: viewer={req.ViewerName}");
                    await Task.Delay(200);
                    GrantFileTransfer = cfg.AllowFileTransfer;
                    GrantClipboard = cfg.AllowClipboard;
                    Log("Calling ApproveSession");
                    await ApproveSession();
                    Log($"ApproveSession done. _sessionId={_sessionId} _fileTransferPolicy.IsAllowed={_fileTransferPolicy.IsAllowed}");
                });
            };
            IncomingOffers.CollectionChanged += (s, e) =>
            {
                if (e.NewItems is null) return;
                foreach (IncomingOfferItem offer in e.NewItems)
                {
                    Log($"IncomingOffer auto-accept: {offer.FileName} {offer.SizeBytes}B id={offer.TransferId}");
                    _ = WpfApplication.Current.Dispatcher.InvokeAsync(() => AcceptOffer(offer));
                }
            };
            _host.FileOfferReceived += offer => Log($"[host] FileOfferReceived: {offer.FileName} {offer.SizeBytes} isFromHost={offer.IsFromHost}");
            _host.FileChunkReceived += c => Log($"[host] FileChunkReceived: id={c.TransferId} idx={c.ChunkIndex} size={c.Data.Length}");
            _host.FileCompleteReceived += id => Log($"[host] FileCompleteReceived: {id}");
            _host.FileDecisionReceived += d => Log($"[host] FileDecisionReceived: {d.TransferId} accepted={d.Accepted}");
        }
        if (cfg.Mode == "viewer-send" && cfg.SendFiles is { Length: > 0 } && !string.IsNullOrWhiteSpace(cfg.ConnectCode))
        {
            ConnectCode = cfg.ConnectCode;
            foreach (var path in cfg.SendFiles)
            {
                if (File.Exists(path)) LocalFiles.Add(new LocalFileEntry(path));
            }
            Log($"viewer-send: ConnectCode={ConnectCode}, LocalFiles count={LocalFiles.Count}");

            // Bu method zaten UI thread'inde (constructor MainWindow tarafından çağrılıyor).
            // await'ler SynchronizationContext ile UI thread'inde devam eder.
            await Task.Delay(800);
            Log("Calling RequestSession (direct, no Dispatcher.InvokeAsync)");
            try
            {
                await RequestSession();
                Log($"RequestSession completed. _sessionId={_sessionId} FT.IsAllowed={_fileTransferPolicy.IsAllowed}");
            }
            catch (Exception ex)
            {
                Log($"RequestSession threw: {ex.GetType().Name}: {ex.Message}");
            }

            while (_sessionId is null || !_fileTransferPolicy.IsAllowed)
            {
                await Task.Delay(150);
            }
            await Task.Delay(200);
            Log("Calling SendSelected");
            SendSelectedCommand.Execute(null);
            Log("SendSelected called");

            _viewer.FileOfferReceived += offer => Log($"[viewer] FileOfferReceived: {offer.FileName} {offer.SizeBytes} isFromHost={offer.IsFromHost}");
            _viewer.FileDecisionReceived += d => Log($"[viewer] FileDecisionReceived: {d.TransferId} accepted={d.Accepted}");
            _viewer.FileChunkReceived += c => Log($"[viewer] FileChunkReceived: id={c.TransferId} idx={c.ChunkIndex} size={c.Data.Length}");
            _viewer.FileCompleteReceived += id => Log($"[viewer] FileCompleteReceived: {id}");
        }
    }

    [RelayCommand]
    private void ShowHost()
    {
        SetPage("host");
        _ = StartHostIfNeededAsync();
    }

    [RelayCommand]
    private void ShowViewer() => SetPage("viewer");

    [RelayCommand]
    private void ShowDevices() => SetPage("devices");

    [RelayCommand]
    private void ShowSessions() => SetPage("sessions");

    [RelayCommand]
    private void ShowFileTransfer() => SetPage("file");

    [RelayCommand]
    private void ShowSecurity() => SetPage("security");

    [RelayCommand]
    private void ShowSettings() => SetPage("settings");

    private void ShowRemote() => SetPage("remote");

    [RelayCommand]
    private void CopyCode()
    {
        System.Windows.Clipboard.SetText(PairingCode);
        SecurityEvents.Insert(0, "14:32  Info     CODE_COPIED                Bağlantı kodu panoya kopyalandı");
    }

    [RelayCommand]
    private async Task RefreshCode()
    {
        await StartHostIfNeededAsync();
        PairingCode = _host.ConnectAddress;
        SecurityEvents.Insert(0, "14:32  Info     PAIRING_CODE_REFRESHED     Bağlantı kodu yenilendi");
    }

    [RelayCommand]
    private async Task RequestSession()
    {
        var cfg = UiTest;
        void Log(string m) => App.UiTestLog(cfg, m);
        Log($"[RS] entered RequestSession, cfg-null={cfg is null}, _sessionId-before={_sessionId}");
        try
        {
            ViewerSurfaceText = "Host kullanıcısının onayı bekleniyor.";
            MetricsText = "Host kullanıcısının onayı bekleniyor.";
            SecurityEvents.Insert(0, "14:32  Info     SESSION_REQUESTED          Bağlantı isteği gönderildi");
            Log($"[RS] ConnectCode={ConnectCode}");
            await _viewer.ConnectAsync(ConnectCode, CancellationToken.None);
            Log($"[RS] after ConnectAsync. grants RC={_viewer.RemoteControlGranted} FT={_viewer.FileTransferGranted} CB={_viewer.ClipboardGranted}");
            _sessionId = Guid.NewGuid();
            _controlPolicy.Enable(_sessionId.Value, _viewer.RemoteControlGranted);
            _fileTransferPolicy.Enable(_sessionId.Value, _viewer.FileTransferGranted);
            _clipboardPolicy.Enable(_sessionId.Value, _viewer.ClipboardGranted);
            Log($"[RS] policies enabled. _sessionId={_sessionId} FT.IsAllowed={_fileTransferPolicy.IsAllowed}");
            RemoteControlStatusText = _viewer.RemoteControlGranted ? "Açık" : "Kapalı";
            FileTransferStatusText = _viewer.FileTransferGranted ? "Açık" : "Kapalı";
            ClipboardStatusText = _viewer.ClipboardGranted ? "Açık" : "Kapalı";
        }
        catch (Exception ex)
        {
            Log($"[RS] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null) Log($"[RS]   Inner: {ex.InnerException.Message}");
            ViewerSurfaceText = "Bağlantı kurulamadı";
            MetricsText = $"Hata: {ex.Message}";
            SecurityEvents.Insert(0, $"14:32  Warning  CONNECTION_FAILED          {ex.Message}");
        }
        Log($"[RS] exiting. _sessionId={_sessionId} FT.IsAllowed={_fileTransferPolicy.IsAllowed}");
    }

    [RelayCommand]
    private async Task ApproveSession()
    {
        try
        {
            _sessionId = Guid.NewGuid();
            _controlPolicy.Enable(_sessionId.Value, remoteControlApproved: GrantRemoteControl);
            _fileTransferPolicy.Enable(_sessionId.Value, fileTransferApproved: GrantFileTransfer);
            _clipboardPolicy.Enable(_sessionId.Value, clipboardApproved: GrantClipboard);
            await _host.ApproveAsync(GrantRemoteControl, GrantFileTransfer, GrantClipboard, CancellationToken.None);
            IncomingModalVisibility = Visibility.Collapsed;
            HostStatusTitle = "Bu bilgisayar şu anda uzaktan görüntüleniyor";
            IncomingRequestText = "Oturum onaylandı. Bağlantıyı istediğiniz anda kesebilirsiniz.";
            TopSessionText = "Aktif oturum: Bağlı";
            SessionDotBrush = Green;
            RemoteControlStatusText = GrantRemoteControl ? "Açık" : "Kapalı";
            FileTransferStatusText = GrantFileTransfer ? "Açık" : "Kapalı";
            ClipboardStatusText = GrantClipboard ? "Açık" : "Kapalı";
            SecurityEvents.Insert(0, "14:32  Info     SESSION_APPROVED           Host kullanıcısı açık onay verdi");
            if (GrantRemoteControl)
                SecurityEvents.Insert(0, "14:32  Info     REMOTE_CONTROL_ENABLED     Uzaktan kontrol izni verildi");
            if (GrantFileTransfer)
                SecurityEvents.Insert(0, "14:32  Info     FILE_TRANSFER_ENABLED      Dosya aktarımı izni verildi");
            if (GrantClipboard)
                SecurityEvents.Insert(0, "14:32  Info     CLIPBOARD_ENABLED          Clipboard izni verildi");
            ShowSessions();
        }
        catch (Exception ex)
        {
            IncomingRequestText = $"Onay başarısız: {ex.Message}";
            SecurityEvents.Insert(0, $"14:32  Warning  APPROVE_FAILED             {ex.Message}");
        }
    }

    [RelayCommand]
    private void StopControl()
    {
        if (_sessionId is null) return;
        _controlPolicy.Enable(_sessionId.Value, false);
        RemoteControlStatusText = "Kapalı";
        SecurityEvents.Insert(0, "14:32  Info     REMOTE_CONTROL_DISABLED    Host uzaktan kontrolü durdurdu");
    }

    [RelayCommand]
    private void StopFileTransfer()
    {
        if (_sessionId is null) return;
        _fileTransferPolicy.Enable(_sessionId.Value, false);
        FileTransferStatusText = "Kapalı";
        SecurityEvents.Insert(0, "14:32  Info     FILE_TRANSFER_DISABLED     Host dosya aktarımını kapattı");
    }

    [RelayCommand]
    private void StopClipboard()
    {
        if (_sessionId is null) return;
        _clipboardPolicy.Enable(_sessionId.Value, false);
        ClipboardStatusText = "Kapalı";
        SecurityEvents.Insert(0, "14:32  Info     CLIPBOARD_DISABLED         Host clipboard'ı kapattı");
    }

    [RelayCommand]
    private void PickFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Title = "Aktarılacak dosyaları seçin"
        };
        if (dialog.ShowDialog() == true)
        {
            foreach (var path in dialog.FileNames)
            {
                if (!LocalFiles.Any(f => string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                    LocalFiles.Add(new LocalFileEntry(path));
            }
        }
    }

    [RelayCommand]
    private void RemoveLocalFile(LocalFileEntry? entry)
    {
        if (entry is null) return;
        LocalFiles.Remove(entry);
    }

    [RelayCommand]
    private void SendSelected()
    {
        if (_sessionId is null) { SecurityEvents.Insert(0, "14:32  Warning  FILE_SEND_REJECTED         Aktif oturum yok"); return; }
        if (!_fileTransferPolicy.IsAllowed) { SecurityEvents.Insert(0, "14:32  Warning  FILE_SEND_REJECTED         Dosya aktarımı izni kapalı"); return; }
        var snapshot = LocalFiles.ToList();
        foreach (var file in snapshot)
        {
            _ = SendFileAsync(file);
        }
        LocalFiles.Clear();
    }

    private async Task SendFileAsync(LocalFileEntry file)
    {
        var cfg = UiTest;
        void Log(string m) => App.UiTestLog(cfg, m);
        if (_sessionId is null) { Log($"[SFA] SKIP: _sessionId null"); return; }
        var transferId = Guid.NewGuid().ToString("N");
        var item = new TransferQueueItem(transferId, file.DisplayName, file.SizeBytes, "Giden")
        {
            Status = "Onay bekleniyor",
            LocalPath = file.FilePath
        };
        TransferQueue.Insert(0, item);
        Log($"[SFA] queued transferId={transferId} file={file.DisplayName} size={file.SizeBytes} queue.Count={TransferQueue.Count}");
        SecurityEvents.Insert(0, $"14:32  Info     FILE_OFFER_SENT            {file.DisplayName} gönderim için teklif edildi");

        try
        {
            if (IsHostSide)
                await _host.SendFileOfferAsync(transferId, file.DisplayName, file.SizeBytes, CancellationToken.None);
            else
                await _viewer.SendFileOfferAsync(transferId, file.DisplayName, file.SizeBytes, CancellationToken.None);
            Log($"[SFA] SendFileOfferAsync OK. waiting for decision...");
        }
        catch (Exception ex)
        {
            item.Status = "Hata";
            Log($"[SFA] EXCEPTION: {ex.Message}");
            SecurityEvents.Insert(0, $"14:32  Warning  FILE_OFFER_FAILED          {ex.Message}");
        }
    }

    private void HandleFileDecision(IncomingFileDecision decision, bool fromHostSide)
    {
        // FileDecisionReceived sadece transferi gönderen tarafta tetiklenir (karşı tarafın FILE_ACCEPT/REJECT mesajı).
        // Her ShellViewModel instance'ı sadece bir tarafta aktiftir (host veya viewer), bu yüzden guard gereksiz.
        var cfg = UiTest;
        void Log(string m) => App.UiTestLog(cfg, m);
        Log($"[HFD] entered: fromHostSide={fromHostSide} IsHostSide={IsHostSide} decision.TransferId={decision.TransferId} accepted={decision.Accepted} queue.Count={TransferQueue.Count}");
        var outgoing = TransferQueue.FirstOrDefault(t => t.TransferId == decision.TransferId);
        if (outgoing is null) { Log($"[HFD] outgoing NULL, transferId={decision.TransferId}"); return; }
        if (decision.Accepted)
        {
            outgoing.Status = "Aktarılıyor";
            Log($"[HFD] starting StreamOutgoingFileAsync for {outgoing.FileName}");
            _ = StreamOutgoingFileAsync(outgoing);
        }
        else
        {
            outgoing.Status = "Reddedildi";
            SecurityEvents.Insert(0, $"14:32  Info     FILE_REJECTED              {outgoing.FileName} reddedildi");
        }
    }

    private async Task StreamOutgoingFileAsync(TransferQueueItem item)
    {
        if (item.LocalPath is null || !File.Exists(item.LocalPath)) { item.Status = "Hata: dosya yok"; return; }
        try
        {
            await using var fs = File.OpenRead(item.LocalPath);
            var buffer = new byte[FileChunkSize];
            int read;
            int idx = 0;
            long sent = 0;
            while ((read = await fs.ReadAsync(buffer.AsMemory(0, FileChunkSize))) > 0)
            {
                var payload = new byte[read];
                Buffer.BlockCopy(buffer, 0, payload, 0, read);
                if (IsHostSide)
                    await _host.SendFileChunkAsync(item.TransferId, idx, payload, CancellationToken.None);
                else
                    await _viewer.SendFileChunkAsync(item.TransferId, idx, payload, CancellationToken.None);
                idx++;
                sent += read;
                item.Progress = item.SizeBytes > 0 ? (double)sent / item.SizeBytes : 0;
            }

            if (IsHostSide)
                await _host.SendFileCompleteAsync(item.TransferId, CancellationToken.None);
            else
                await _viewer.SendFileCompleteAsync(item.TransferId, CancellationToken.None);

            item.Status = "Tamamlandı";
            item.Progress = 1.0;
            SecurityEvents.Insert(0, $"14:32  Info     FILE_SENT                  {item.FileName} gönderildi");
        }
        catch (Exception ex)
        {
            item.Status = $"Hata: {ex.Message}";
            SecurityEvents.Insert(0, $"14:32  Warning  FILE_SEND_FAILED           {ex.Message}");
        }
    }

    private void HandleIncomingOffer(IncomingFileOffer offer, bool fromHostSide)
    {
        if (_sessionId is null || !_fileTransferPolicy.IsAllowed) return;
        // FileOfferReceived sadece alan tarafta tetiklenir; guard gereksiz

        var item = new IncomingOfferItem(offer.TransferId, offer.FileName, offer.SizeBytes, offer.IsFromHost);
        IncomingOffers.Insert(0, item);
        SecurityEvents.Insert(0, $"14:32  Info     FILE_OFFER_RECEIVED        {offer.FileName} ({item.SizeText}) gelen teklif");
    }

    [RelayCommand]
    private async Task AcceptOffer(IncomingOfferItem? offer)
    {
        if (offer is null) return;
        if (_sessionId is null || !_fileTransferPolicy.IsAllowed) { offer.Status = "İzin yok"; return; }
        offer.Status = "Kabul edildi";

        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "SecureRemoteDesk");
        Directory.CreateDirectory(downloads);
        var safeName = SanitizeFileName(offer.FileName);
        var targetPath = Path.Combine(downloads, safeName);
        try
        {
            var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            _incomingFiles[offer.TransferId] = stream;
        }
        catch (Exception ex)
        {
            offer.Status = $"Hata: {ex.Message}";
            return;
        }

        var queue = new TransferQueueItem(offer.TransferId, offer.FileName, offer.SizeBytes, "Gelen")
        {
            Status = "Aktarılıyor",
            LocalPath = targetPath
        };
        TransferQueue.Insert(0, queue);

        try
        {
            if (IsHostSide)
                await _host.SendFileDecisionAsync(offer.TransferId, true, CancellationToken.None);
            else
                await _viewer.SendFileDecisionAsync(offer.TransferId, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            queue.Status = $"Hata: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RejectOffer(IncomingOfferItem? offer)
    {
        if (offer is null) return;
        offer.Status = "Reddedildi";
        try
        {
            if (IsHostSide)
                await _host.SendFileDecisionAsync(offer.TransferId, false, CancellationToken.None);
            else
                await _viewer.SendFileDecisionAsync(offer.TransferId, false, CancellationToken.None);
            SecurityEvents.Insert(0, $"14:32  Info     FILE_REJECTED              {offer.FileName} reddedildi");
        }
        catch { }
    }

    private void HandleIncomingChunk(FileChunkPayload chunk, bool fromHostSide)
    {
        // FileChunkReceived sadece alan tarafta tetiklenir
        if (!_incomingFiles.TryGetValue(chunk.TransferId, out var stream)) return;
        try
        {
            stream.Write(chunk.Data, 0, chunk.Data.Length);
            var item = TransferQueue.FirstOrDefault(t => t.TransferId == chunk.TransferId);
            if (item is not null && item.SizeBytes > 0)
            {
                var pos = stream.Position;
                item.Progress = Math.Min(1.0, (double)pos / item.SizeBytes);
            }
        }
        catch (Exception ex)
        {
            SecurityEvents.Insert(0, $"14:32  Warning  FILE_CHUNK_FAILED          {ex.Message}");
        }
    }

    private void HandleFileComplete(string transferId, bool fromHostSide)
    {
        // FileCompleteReceived sadece alan tarafta tetiklenir
        if (_incomingFiles.TryGetValue(transferId, out var stream))
        {
            stream.Flush();
            stream.Dispose();
            _incomingFiles.Remove(transferId);
        }
        var item = TransferQueue.FirstOrDefault(t => t.TransferId == transferId);
        if (item is not null)
        {
            item.Progress = 1.0;
            item.Status = "Tamamlandı";
            SecurityEvents.Insert(0, $"14:32  Info     FILE_RECEIVED              {item.FileName} kaydedildi: {item.LocalPath}");
        }
        var offer = IncomingOffers.FirstOrDefault(o => o.TransferId == transferId);
        if (offer is not null) offer.Status = "Tamamlandı";
    }

    [RelayCommand]
    private void SendClipboard()
    {
        if (_sessionId is null) { SecurityEvents.Insert(0, "14:32  Warning  CLIPBOARD_REJECTED         Aktif oturum yok"); return; }
        if (!_clipboardPolicy.IsAllowed) { SecurityEvents.Insert(0, "14:32  Warning  CLIPBOARD_REJECTED         Clipboard izni kapalı"); return; }
        string text;
        try { text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : ""; }
        catch (Exception ex) { SecurityEvents.Insert(0, $"14:32  Warning  CLIPBOARD_READ_FAILED      {ex.Message}"); return; }
        if (string.IsNullOrEmpty(text)) { SecurityEvents.Insert(0, "14:32  Warning  CLIPBOARD_EMPTY            Pano boş"); return; }
        _ = SendClipboardInternalAsync(text);
    }

    private async Task SendClipboardInternalAsync(string text)
    {
        try
        {
            if (IsHostSide)
                await _host.SendClipboardAsync(text, CancellationToken.None);
            else
                await _viewer.SendClipboardAsync(text, CancellationToken.None);
            ClipboardPreviewText = text.Length > 200 ? text[..200] + "…" : text;
            SecurityEvents.Insert(0, $"14:32  Info     CLIPBOARD_SENT             Pano içeriği gönderildi ({text.Length} karakter)");
        }
        catch (Exception ex)
        {
            SecurityEvents.Insert(0, $"14:32  Warning  CLIPBOARD_SEND_FAILED      {ex.Message}");
        }
    }

    [RelayCommand]
    private void ReceiveClipboard()
    {
        if (_sessionId is null) { SecurityEvents.Insert(0, "14:32  Warning  CLIPBOARD_REJECTED         Aktif oturum yok"); return; }
        if (!_clipboardPolicy.IsAllowed) { SecurityEvents.Insert(0, "14:32  Warning  CLIPBOARD_REJECTED         Clipboard izni kapalı"); return; }
        // Karşı taraftan kendi panosunu göndermesini iste
        _ = SendClipboardInternalAsync("__REQUEST_PEER_CLIPBOARD__");
    }

    private void HandleClipboardReceived(string text, bool fromHostSide)
    {
        // ClipboardReceived sadece alan tarafta tetiklenir
        if (text == "__REQUEST_PEER_CLIPBOARD__")
        {
            // Karşı taraf benim panomu istedi
            try
            {
                if (System.Windows.Clipboard.ContainsText())
                {
                    var mine = System.Windows.Clipboard.GetText();
                    _ = SendClipboardInternalAsync(mine);
                    SecurityEvents.Insert(0, "14:32  Info     CLIPBOARD_REQUEST_HANDLED  Karşı taraf panomu istedi, gönderildi");
                }
            }
            catch { }
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(text);
            ClipboardPreviewText = text.Length > 200 ? text[..200] + "…" : text;
            SecurityEvents.Insert(0, $"14:32  Info     CLIPBOARD_RECEIVED         Pano içeriği alındı ({text.Length} karakter)");
        }
        catch (Exception ex)
        {
            SecurityEvents.Insert(0, $"14:32  Warning  CLIPBOARD_SET_FAILED       {ex.Message}");
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (clean.Length > 120) clean = clean[..120];
        return string.IsNullOrWhiteSpace(clean) ? "dosya" : clean;
    }

    private bool IsHostSide => _sessionId is not null && _host.ConnectAddress == PairingCode;

    public async Task SendPointerMoveAsync(double nx, double ny) => await _viewer.SendPointerMoveAsync(nx, ny);
    public async Task SendPointerButtonAsync(bool left, bool down) => await _viewer.SendPointerButtonAsync(left, down);
    public async Task SendWheelAsync(int delta) => await _viewer.SendWheelAsync(delta);
    public async Task SendKeyAsync(int virtualKey, bool down) => await _viewer.SendKeyAsync(virtualKey, down);
    public bool IsRemoteControlGranted => _viewer.RemoteControlGranted;
    public int RemoteScreenPixelWidth => _viewer.RemoteScreenWidth;
    public int RemoteScreenPixelHeight => _viewer.RemoteScreenHeight;

    private void ApplyRemoteInput(string message)
    {
        if (!_controlPolicy.IsRemoteControlEnabled)
            return;

        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        var parts = message.Split('|');
        switch (parts[0])
        {
            case "MOVE" when parts.Length == 3
                && double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var nx)
                && double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var ny):
                Services.Win32Input.MoveTo((int)(nx * bounds.Width), (int)(ny * bounds.Height));
                break;
            case "DOWN" when parts.Length == 2:
                Services.Win32Input.MouseButton(left: parts[1] == "L", down: true);
                break;
            case "UP" when parts.Length == 2:
                Services.Win32Input.MouseButton(left: parts[1] == "L", down: false);
                break;
            case "WHEEL" when parts.Length == 2 && int.TryParse(parts[1], out var delta):
                Services.Win32Input.MouseWheel(delta);
                break;
            case "KEYDOWN" when parts.Length == 2 && int.TryParse(parts[1], out var vkDown):
                Services.Win32Input.Key(vkDown, down: true);
                break;
            case "KEYUP" when parts.Length == 2 && int.TryParse(parts[1], out var vkUp):
                Services.Win32Input.Key(vkUp, down: false);
                break;
        }
    }

    [RelayCommand]
    private async Task RejectSession()
    {
        _controlPolicy.Disable();
        _fileTransferPolicy.Disable();
        _clipboardPolicy.Disable();
        await _host.RejectAsync(CancellationToken.None);
        IncomingModalVisibility = Visibility.Collapsed;
        IncomingRequestText = "Bağlantı isteği reddedildi.";
        HostStatusTitle = "Bağlantı isteği bekleniyor";
        SecurityEvents.Insert(0, "14:32  Info     SESSION_REJECTED           Host kullanıcısı isteği reddetti");
    }

    [RelayCommand]
    private async Task EndSession()
    {
        foreach (var fs in _incomingFiles.Values) { try { fs.Dispose(); } catch { } }
        _incomingFiles.Clear();
        _controlPolicy.Disable();
        _fileTransferPolicy.Disable();
        _clipboardPolicy.Disable();
        _sessionId = null;
        await _host.EndAsync();
        TopSessionText = "Aktif oturum: Yok";
        SessionDotBrush = Gray;
        RemoteControlStatusText = "Kapalı";
        FileTransferStatusText = "Kapalı";
        ClipboardStatusText = "Kapalı";
        HostStatusTitle = "Bağlantı isteği bekleniyor";
        ViewerSurfaceText = "Uzak ekran burada gösterilecek";
        RemoteFrame = null;
        SecurityEvents.Insert(0, "14:32  Info     SESSION_ENDED              Oturum sonlandırıldı");
        ShowHost();
    }

    private async Task StartHostIfNeededAsync()
    {
        if (_hostStarted)
            return;

        try
        {
            await _host.StartAsync();
            _hostStarted = true;
            PairingCode = _host.ConnectAddress;
        }
        catch (Exception ex)
        {
            PairingCode = "Host portu kullanılamıyor";
            IncomingRequestText = "Bu pencere Host olarak dinleyemiyor. Viewer modu yine kullanılabilir.";
            SecurityEvents.Insert(0, $"14:32  Warning  HOST_LISTEN_FAILED         {ex.Message}");
        }
    }

    private void SetPage(string page)
    {
        HostVisibility = page == "host" ? Visibility.Visible : Visibility.Collapsed;
        ViewerVisibility = page == "viewer" ? Visibility.Visible : Visibility.Collapsed;
        RemoteVisibility = page == "remote" ? Visibility.Visible : Visibility.Collapsed;
        SessionsVisibility = page == "sessions" ? Visibility.Visible : Visibility.Collapsed;
        DevicesVisibility = page == "devices" ? Visibility.Visible : Visibility.Collapsed;
        FileTransferVisibility = page == "file" ? Visibility.Visible : Visibility.Collapsed;
        SecurityVisibility = page == "security" ? Visibility.Visible : Visibility.Collapsed;
        SettingsVisibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;

        HostNavBrush = page == "host" ? ActiveNav : InactiveNav;
        ViewerNavBrush = page is "viewer" or "remote" ? ActiveNav : InactiveNav;
        DevicesNavBrush = page == "devices" ? ActiveNav : InactiveNav;
        SessionsNavBrush = page == "sessions" ? ActiveNav : InactiveNav;
        FileTransferNavBrush = page == "file" ? ActiveNav : InactiveNav;
        SecurityNavBrush = page == "security" ? ActiveNav : InactiveNav;
        SettingsNavBrush = page == "settings" ? ActiveNav : InactiveNav;
    }
}
