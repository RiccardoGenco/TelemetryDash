namespace TelemetryDash.Core.Models;

public class TelemetrySession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string DataSourceName { get; set; } = string.Empty;
    public List<TelemetryReading> Readings { get; set; } = new();
    public List<AlarmResult> Alarms { get; set; } = new();
}
