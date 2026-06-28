using TelemetryDash.Core.Models;

namespace TelemetryDash.Core.Interfaces;

public interface ISessionRepository
{
    Task<Guid> CreateSessionAsync(TelemetrySession session);
    Task SaveReadingsAsync(Guid sessionId, IEnumerable<TelemetryReading> readings);
    Task SaveAlarmAsync(Guid sessionId, AlarmResult alarm);
    Task EndSessionAsync(Guid sessionId, DateTime endTime);
    Task<TelemetrySession?> GetSessionAsync(Guid sessionId);
    Task<IReadOnlyList<TelemetrySession>> GetAllSessionsAsync();
}
