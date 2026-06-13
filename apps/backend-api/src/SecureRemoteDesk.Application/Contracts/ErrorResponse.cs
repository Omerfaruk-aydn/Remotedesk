namespace SecureRemoteDesk.Application.Contracts;

public sealed record ErrorEnvelope(ErrorBody Error);
public sealed record ErrorBody(string Code, string Message, object? Details = null);
