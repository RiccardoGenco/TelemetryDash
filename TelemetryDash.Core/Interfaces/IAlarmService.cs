using TelemetryDash.Core.Models;

namespace TelemetryDash.Core.Interfaces;

public interface IAlarmService
{
    AlarmResult Evaluate(TelemetryReading reading);
    IReadOnlyList<AlarmResult> GetActiveAlarms();
    void ClearAlarms();
    void AddConfig(AlarmConfig config);
}
