using System.Collections.ObjectModel;
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

public partial class ShellViewModel : ObservableObject
{
    private static readonly MediaBrush ActiveNav = new SolidColorBrush(MediaColor.FromRgb(24, 69, 108));
    private static readonly MediaBrush InactiveNav = MediaBrushes.Transparent;
    private static readonly MediaBrush Green = new SolidColorBrush(MediaColor.FromRgb(34, 197, 94));
    private static readonly MediaBrush Gray = new SolidColorBrush(MediaColor.FromRgb(107, 114, 128));

    private readonly GuardedRemoteControlPolicy _controlPolicy = new();
    private readonly LocalHostSessionServer _host = new();
    private readonly LocalViewerSessionClient _viewer = new();
    private bool _hostStarted;

    [ObservableProperty] private string pairingCode = "Hazırlanıyor";
    [ObservableProperty] private string connectCode = "";
    [ObservableProperty] private string incomingRequestText = "Viewer modunda bağlantı kodu girildiğinde istek burada görünecek.";
    [ObservableProperty] private string hostStatusTitle = "Bağlantı isteği bekleniyor";
    [ObservableProperty] private string viewerSurfaceText = "Uzak ekran burada gösterilecek";
    [ObservableProperty] private string metricsText = "Son bağlantı isteği zaman aşımına uğradı veya reddedildi.";
    [ObservableProperty] private string topSessionText = "Aktif oturum: Yok";
    [ObservableProperty] private MediaBrush sessionDotBrush = Gray;
    [ObservableProperty] private BitmapImage? remoteFrame;

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
            IncomingModalVisibility = Visibility.Visible;
            SecurityEvents.Insert(0, "14:32  Info     SESSION_REQUESTED          Host tarafına bağlantı isteği geldi");
            ShowHost();
        });

        _host.StatusChanged += message => WpfApplication.Current.Dispatcher.Invoke(() =>
        {
            HostStatusTitle = message;
            SecurityEvents.Insert(0, $"14:32  Info     HOST_STATUS                {message}");
        });

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

        _ = StartHostIfNeededAsync();
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
        try
        {
            ViewerSurfaceText = "Host kullanıcısının onayı bekleniyor.";
            MetricsText = "Host kullanıcısının onayı bekleniyor.";
            SecurityEvents.Insert(0, "14:32  Info     SESSION_REQUESTED          Bağlantı isteği gönderildi");
            await _viewer.ConnectAsync(ConnectCode, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ViewerSurfaceText = "Bağlantı kurulamadı";
            MetricsText = $"Hata: {ex.Message}";
            SecurityEvents.Insert(0, $"14:32  Warning  CONNECTION_FAILED          {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ApproveSession()
    {
        try
        {
            _controlPolicy.Enable(Guid.NewGuid(), remoteControlApproved: false);
            await _host.ApproveAsync(CancellationToken.None);
            IncomingModalVisibility = Visibility.Collapsed;
            HostStatusTitle = "Bu bilgisayar şu anda uzaktan görüntüleniyor";
            IncomingRequestText = "Oturum onaylandı. Bağlantıyı istediğiniz anda kesebilirsiniz.";
            TopSessionText = "Aktif oturum: Bağlı";
            SessionDotBrush = Green;
            SecurityEvents.Insert(0, "14:32  Info     SESSION_APPROVED           Host kullanıcısı açık onay verdi");
            ShowSessions();
        }
        catch (Exception ex)
        {
            IncomingRequestText = $"Onay başarısız: {ex.Message}";
            SecurityEvents.Insert(0, $"14:32  Warning  APPROVE_FAILED             {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RejectSession()
    {
        _controlPolicy.Disable();
        await _host.RejectAsync(CancellationToken.None);
        IncomingModalVisibility = Visibility.Collapsed;
        IncomingRequestText = "Bağlantı isteği reddedildi.";
        HostStatusTitle = "Bağlantı isteği bekleniyor";
        SecurityEvents.Insert(0, "14:32  Info     SESSION_REJECTED           Host kullanıcısı isteği reddetti");
    }

    [RelayCommand]
    private async Task EndSession()
    {
        _controlPolicy.Disable();
        await _host.EndAsync();
        TopSessionText = "Aktif oturum: Yok";
        SessionDotBrush = Gray;
        HostStatusTitle = "Bağlantı isteği bekleniyor";
        ViewerSurfaceText = "Uzak ekran burada gösterilecek";
        RemoteFrame = null;
        SecurityEvents.Insert(0, "14:32  Info     SESSION_ENDED              Oturum sonlandırıldı ve input kapatıldı");
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
