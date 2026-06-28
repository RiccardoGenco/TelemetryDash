using TelemetryDash.Core.Enums;

namespace TelemetryDash.Core.Models;

public class TelemetryReading
{
    public string ChannelId { get; set; } = string.Empty;
    public double Value { get; set; }
    public DateTime Timestamp { get; set; }
    public QualityFlag Quality { get; set; }
}
