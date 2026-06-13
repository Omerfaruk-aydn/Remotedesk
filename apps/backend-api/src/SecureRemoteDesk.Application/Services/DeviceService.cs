using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SecureRemoteDesk.Application.Contracts;
using SecureRemoteDesk.Application.Interfaces;
using SecureRemoteDesk.Domain.Entities;
using SecureRemoteDesk.Domain.Enums;

namespace SecureRemoteDesk.Application.Services;

public sealed class DeviceService
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IAuditWriter _audit;

    public DeviceService(IAppDbContext db, ITokenService tokens, IAuditWriter audit)
    {
        _db = db;
        _tokens = tokens;
        _audit = audit;
    }

    public async Task<DeviceDto> RegisterAsync(Guid ownerUserId, DeviceRegisterRequest request, CancellationToken cancellationToken)
    {
        var device = new Device
        {
            OwnerUserId = ownerUserId,
            DeviceName = request.DeviceName.Trim(),
            DeviceType = request.DeviceType.Trim(),
            PublicDeviceId = $"srd_{Guid.NewGuid():N}",
            FingerprintHash = _tokens.HashToken(request.Fingerprint),
            OsName = request.OsName,
            OsVersion = request.OsVersion,
            AppVersion = request.AppVersion,
            LastSeenAt = DateTimeOffset.UtcNow
        };
        _db.Devices.Add(device);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(AuditAction.DeviceRegistered, "Device", device.Id.ToString(), ownerUserId, device.Id, cancellationToken);
        return ToDto(device);
    }

    public async Task<IReadOnlyList<DeviceDto>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var devices = await _db.Devices.AsNoTracking().Where(x => x.OwnerUserId == ownerUserId && !x.IsRevoked).ToListAsync(cancellationToken);
        return devices.Select(ToDto).ToList();
    }

    public async Task<PairingCodeResponse> CreatePairingCodeAsync(Guid ownerUserId, Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await _db.Devices.SingleAsync(x => x.Id == deviceId && x.OwnerUserId == ownerUserId && !x.IsRevoked, cancellationToken);
        var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        var pairing = new PairingCode
        {
            DeviceId = device.Id,
            CodeHash = _tokens.HashToken(code),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        _db.PairingCodes.Add(pairing);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(AuditAction.PairingCodeCreated, "Device", device.Id.ToString(), ownerUserId, device.Id, cancellationToken);
        return new PairingCodeResponse(code, pairing.ExpiresAt);
    }

    public async Task<DeviceDto> PairAsync(Guid viewerUserId, PairDeviceRequest request, CancellationToken cancellationToken)
    {
        var codeHash = _tokens.HashToken(request.Code);
        var pairing = await _db.PairingCodes
            .Include(x => x.Device)
            .Where(x => x.CodeHash == codeHash && x.UsedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (pairing?.Device is null || pairing.Device.IsRevoked)
        {
            await _audit.WriteAsync(AuditAction.PairingCodeFailed, "PairingCode", null, viewerUserId, request.ViewerDeviceId, cancellationToken);
            throw new InvalidOperationException("Invalid or expired pairing code.");
        }

        pairing.UsedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(pairing.Device);
    }

    private static DeviceDto ToDto(Device device) => new(device.Id, device.PublicDeviceId, device.DeviceName, device.DeviceType, device.IsRevoked, device.LastSeenAt);
}
