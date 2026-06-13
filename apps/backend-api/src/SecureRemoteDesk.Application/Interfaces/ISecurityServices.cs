using SecureRemoteDesk.Domain.Entities;

namespace SecureRemoteDesk.Application.Interfaces;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface ITokenService
{
    string CreateAccessToken(User user, DateTimeOffset expiresAt);
    string CreateSecureToken();
    string HashToken(string token);
}

public interface IAuditWriter
{
    Task WriteAsync(string action, string resourceType, string? resourceId, Guid? actorUserId, Guid? actorDeviceId, CancellationToken cancellationToken);
}
