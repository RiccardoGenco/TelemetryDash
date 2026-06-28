using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Models;

namespace TelemetryDash.Core.Interfaces;

public interface IPlaybackService
{
    Task<TelemetrySession> LoadSessionAsync(Guid sessionId);
    Task<IReadOnlyList<TelemetrySession>> GetSessionsAsync();
    void Play();
    void Pause();
    void Stop();
    void Seek(DateTime timestamp);
    void SetSpeed(double multiplier);
    PlaybackState State { get; }
    double Speed { get; }
    event Action<TelemetryReading>? OnReading;
    event Action<PlaybackState>? OnStateChanged;
}
