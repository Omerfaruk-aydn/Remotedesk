using Microsoft.EntityFrameworkCore;
using SecureRemoteDesk.Application.Contracts;
using SecureRemoteDesk.Application.Interfaces;
using SecureRemoteDesk.Domain.Entities;
using SecureRemoteDesk.Domain.Enums;

namespace SecureRemoteDesk.Application.Services;

public sealed class AuthService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokens;
    private readonly IAuditWriter _audit;

    public AuthService(IAppDbContext db, IPasswordHasher passwordHasher, ITokenService tokens, IAuditWriter audit)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokens = tokens;
        _audit = audit;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(x => x.Email == email, cancellationToken))
            throw new InvalidOperationException("Email is already registered.");

        var user = new User
        {
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password)
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(AuditAction.UserRegistered, "User", user.Id.ToString(), user.Id, null, cancellationToken);
        return await CreateAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null || user.DisabledAt is not null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await _audit.WriteAsync(AuditAction.UserLoginFailed, "User", email, null, null, cancellationToken);
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _audit.WriteAsync(AuditAction.UserLoginSuccess, "User", user.Id.ToString(), user.Id, null, cancellationToken);
        return await CreateAuthResponseAsync(user, cancellationToken);
    }

    private async Task<AuthResponse> CreateAuthResponseAsync(User user, CancellationToken cancellationToken)
    {
        var accessExpires = DateTimeOffset.UtcNow.AddMinutes(15);
        var refreshToken = _tokens.CreateSecureToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokens.HashToken(refreshToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14)
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new AuthResponse(_tokens.CreateAccessToken(user, accessExpires), refreshToken, accessExpires, new UserDto(user.Id, user.Email, user.DisplayName, user.Role.ToString()));
    }
}
