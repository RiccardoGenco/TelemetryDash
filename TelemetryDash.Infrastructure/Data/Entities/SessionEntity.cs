namespace TelemetryDash.Infrastructure.Data.Entities;

public class SessionEntity
{
    public Guid Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string DataSourceName { get; set; } = string.Empty;
    public List<ReadingEntity> Readings { get; set; } = new();
    public List<AlarmEntity> Alarms { get; set; } = new();
}
