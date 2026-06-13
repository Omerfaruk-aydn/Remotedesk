using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecureRemoteDesk.Application.Interfaces;
using SecureRemoteDesk.Domain.Entities;

namespace SecureRemoteDesk.Infrastructure.Security;

public sealed class JwtOptions
{
    public required string AccessSecret { get; init; }
    public required string RefreshSecret { get; init; }
    public string Issuer { get; init; } = "SecureRemoteDesk";
    public string Audience { get; init; } = "SecureRemoteDesk.Clients";
}

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public string CreateAccessToken(User user, DateTimeOffset expiresAt)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.AccessSecret)), SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, expires: expiresAt.UtcDateTime, signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        return Convert.ToBase64String(bytes);
    }

    public string HashToken(string token)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.RefreshSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(token)));
    }
}
