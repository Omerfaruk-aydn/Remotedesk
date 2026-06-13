using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SecureRemoteDesk.Api.Hubs;

[Authorize]
public sealed class SignalingHub : Hub
{
    public async Task DeviceOnline(string deviceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}");
        await Clients.Others.SendAsync("device.statusChanged", new { deviceId, online = true, at = DateTimeOffset.UtcNow });
    }

    public Task SendToDevice(string targetDeviceId, string type, object payload)
    {
        var allowed = type is "session.request" or "session.approve" or "session.reject" or "session.end"
            or "webrtc.offer" or "webrtc.answer" or "webrtc.iceCandidate";
        if (!allowed)
            throw new HubException("Unsupported signaling message.");

        return Clients.Group($"device:{targetDeviceId}").SendAsync(type, payload);
    }
}
