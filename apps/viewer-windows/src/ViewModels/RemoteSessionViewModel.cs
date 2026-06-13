using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureRemoteDesk.Viewer.Services;

namespace SecureRemoteDesk.Viewer.ViewModels;

public partial class RemoteSessionViewModel : ObservableObject
{
    private readonly IRemoteInputService _input = new RemoteInputService();

    [ObservableProperty] private string metrics = "Status: waiting | FPS: - | Latency: -";

    [RelayCommand]
    private void EndSession()
    {
        _input.Disable();
        Metrics = "Status: ended | FPS: 0 | Latency: -";
    }
}
