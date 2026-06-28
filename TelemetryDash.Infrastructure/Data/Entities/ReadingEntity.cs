namespace TelemetryDash.Infrastructure.Data.Entities;

public class ReadingEntity
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public double Value { get; set; }
    public DateTime Timestamp { get; set; }
    public byte Quality { get; set; }
}
