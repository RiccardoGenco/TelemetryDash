namespace TelemetryDash.Infrastructure.Data.Entities;

public class AlarmEntity
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public int Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double ReadingValue { get; set; }
}
