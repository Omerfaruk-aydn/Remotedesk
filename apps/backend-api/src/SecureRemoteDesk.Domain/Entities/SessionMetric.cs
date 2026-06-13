namespace SecureRemoteDesk.Domain.Entities;

public sealed class SessionMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public int LatencyMs { get; set; }
    public int Fps { get; set; }
    public int BitrateKbps { get; set; }
    public decimal PacketLoss { get; set; }
    public int ReconnectCount { get; set; }
}
