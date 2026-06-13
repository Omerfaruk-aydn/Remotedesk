using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureRemoteDesk.Application.Contracts;
using SecureRemoteDesk.Application.Services;

namespace SecureRemoteDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly DeviceService _devices;

    public DevicesController(DeviceService devices) => _devices = devices;

    [HttpPost("register")]
    public Task<DeviceDto> Register(DeviceRegisterRequest request, CancellationToken cancellationToken) =>
        _devices.RegisterAsync(UserId, request, cancellationToken);

    [HttpGet]
    public Task<IReadOnlyList<DeviceDto>> List(CancellationToken cancellationToken) => _devices.ListAsync(UserId, cancellationToken);

    [HttpPost("{deviceId:guid}/pairing-code")]
    public Task<PairingCodeResponse> PairingCode(Guid deviceId, CancellationToken cancellationToken) =>
        _devices.CreatePairingCodeAsync(UserId, deviceId, cancellationToken);

    [HttpPost("pair")]
    public Task<DeviceDto> Pair(PairDeviceRequest request, CancellationToken cancellationToken) =>
        _devices.PairAsync(UserId, request, cancellationToken);

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
