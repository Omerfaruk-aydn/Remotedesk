namespace SecureRemoteDesk.Application.Contracts;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, UserDto User);
public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role);
