using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureRemoteDesk.Host.Services;

namespace SecureRemoteDesk.Host.ViewModels;

public partial class ActiveSessionViewModel : ObservableObject
{
    private readonly IInputInjectionService _input = new GuardedInputInjectionService();

    [ObservableProperty] private string connectedUser = "Bağlanan kullanıcı: Bekleniyor";
    [ObservableProperty] private string startedAt = "Başlangıç: -";
    [ObservableProperty] private string permissionsSummary = "İzinler: ekran görüntüleme açık, uzaktan kontrol ayrı onay gerektirir";
    [ObservableProperty] private string latencyText = "Latency: -";
    [ObservableProperty] private string fpsText = "FPS: -";
    [ObservableProperty] private string status = "Durum: güvenli bekleme";

    [RelayCommand]
    private void EndSession()
    {
        _input.Disable();
        Status = "Durum: oturum sonlandırıldı";
    }

    [RelayCommand]
    private void DisableControl()
    {
        _input.Disable();
        PermissionsSummary = "İzinler: sadece ekran görüntüleme";
    }

    [RelayCommand]
    private void ViewOnly() => DisableControl();

    [RelayCommand]
    private void DisableFileTransfer() => PermissionsSummary += ", dosya aktarımı kapalı";

    [RelayCommand]
    private void DisableClipboard() => PermissionsSummary += ", clipboard kapalı";
}
