using System.Windows;
using System.Windows.Media.Imaging;
using SecureRemoteDesk.Desktop.ViewModels;
using Image = System.Windows.Controls.Image;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using KeyInterop = System.Windows.Input.KeyInterop;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;

namespace SecureRemoteDesk.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private ShellViewModel? Vm => DataContext as ShellViewModel;

    private static bool TryGetNormalizedPosition(Image image, MouseEventArgs e, out double nx, out double ny)
    {
        nx = ny = 0;
        if (image.Source is not BitmapSource bitmap || image.ActualWidth <= 0 || image.ActualHeight <= 0)
            return false;

        var scale = Math.Min(image.ActualWidth / bitmap.PixelWidth, image.ActualHeight / bitmap.PixelHeight);
        var displayWidth = bitmap.PixelWidth * scale;
        var displayHeight = bitmap.PixelHeight * scale;
        var offsetX = (image.ActualWidth - displayWidth) / 2;
        var offsetY = (image.ActualHeight - displayHeight) / 2;

        var pos = e.GetPosition(image);
        var px = pos.X - offsetX;
        var py = pos.Y - offsetY;
        if (px < 0 || py < 0 || px > displayWidth || py > displayHeight)
            return false;

        nx = px / displayWidth;
        ny = py / displayHeight;
        return true;
    }

    private async void RemoteImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (Vm is null || !Vm.IsRemoteControlGranted)
            return;
        if (TryGetNormalizedPosition((Image)sender, e, out var nx, out var ny))
            await Vm.SendPointerMoveAsync(nx, ny);
    }

    private async void RemoteImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null || !Vm.IsRemoteControlGranted)
            return;
        ((Image)sender).Focus();
        if (TryGetNormalizedPosition((Image)sender, e, out var nx, out var ny))
            await Vm.SendPointerMoveAsync(nx, ny);
        await Vm.SendPointerButtonAsync(left: true, down: true);
    }

    private async void RemoteImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null || !Vm.IsRemoteControlGranted)
            return;
        await Vm.SendPointerButtonAsync(left: true, down: false);
    }

    private async void RemoteImage_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null || !Vm.IsRemoteControlGranted)
            return;
        if (TryGetNormalizedPosition((Image)sender, e, out var nx, out var ny))
            await Vm.SendPointerMoveAsync(nx, ny);
        await Vm.SendPointerButtonAsync(left: false, down: true);
    }

    private async void RemoteImage_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null || !Vm.IsRemoteControlGranted)
            return;
        await Vm.SendPointerButtonAsync(left: false, down: false);
    }

    private async void RemoteImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Vm is null || !Vm.IsRemoteControlGranted)
            return;
        await Vm.SendWheelAsync(e.Delta);
    }

    private async void RemoteImage_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null || !Vm.IsRemoteControlGranted)
            return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk != 0)
            await Vm.SendKeyAsync(vk, down: true);
        e.Handled = true;
    }

    private async void RemoteImage_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (Vm is null || !Vm.IsRemoteControlGranted)
            return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk != 0)
            await Vm.SendKeyAsync(vk, down: false);
        e.Handled = true;
    }
}
