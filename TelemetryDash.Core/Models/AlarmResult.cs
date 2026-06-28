using TelemetryDash.Core.Enums;

namespace TelemetryDash.Core.Models;

public class AlarmResult
{
    public bool IsAlarm { get; set; }
    public AlarmSeverity Severity { get; set; }
    public DateTime Timestamp { get; set; }
    public TelemetryReading Reading { get; set; } = null!;
    public string Message { get; set; } = string.Empty;
}
