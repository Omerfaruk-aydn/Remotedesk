using SecureRemoteDesk.Application.Contracts;
using Xunit;

namespace SecureRemoteDesk.Api.Tests;

public sealed class SessionPermissionsTests
{
    [Fact]
    public void DefaultsKeepRiskyFeaturesDisabled()
    {
        var permissions = new SessionPermissions();
        Assert.True(permissions.ScreenView);
        Assert.False(permissions.RemoteControl);
        Assert.False(permissions.FileTransfer);
        Assert.False(permissions.Clipboard);
        Assert.False(permissions.Audio);
    }
}
