using TelemetryDash.Core.Enums;

namespace TelemetryDash.Core.Models;

public class AlarmConfig
{
    public string ChannelId { get; set; } = string.Empty;
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public AlarmSeverity Severity { get; set; } = AlarmSeverity.Warning;
}
