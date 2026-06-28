using Microsoft.Extensions.Logging;
using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Interfaces;
using TelemetryDash.Core.Models;

namespace TelemetryDash.Services;

public class PlaybackService : IPlaybackService
{
    private readonly ILogger<PlaybackService> _logger;
    private readonly ISessionRepository _repository;
    private TelemetrySession? _session;
    private CancellationTokenSource? _cts;
    private int _currentIndex;

    private PlaybackState _state = PlaybackState.Stopped;
    private double _speed = 1.0;

    public PlaybackState State
    {
        get => _state;
        private set
        {
            _state = value;
            OnStateChanged?.Invoke(value);
        }
    }

    public double Speed
    {
        get => _speed;
        private set => _speed = value;
    }

    public int CurrentIndex => _currentIndex;

    public int TotalReadings => _session?.Readings.Count ?? 0;

    public event Action<TelemetryReading>? OnReading;
    public event Action<PlaybackState>? OnStateChanged;
    public event Action<int>? OnProgress;

    public PlaybackService(ILogger<PlaybackService> logger, ISessionRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<TelemetrySession> LoadSessionAsync(Guid sessionId)
    {
        var session = await _repository.GetSessionAsync(sessionId)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");

        _session = session;
        _session.Readings = _session.Readings.OrderBy(r => r.Timestamp).ToList();
        _currentIndex = 0;
        State = PlaybackState.Stopped;

        _logger.LogInformation("Loaded session {Id} with {Count} readings",
            sessionId, _session.Readings.Count);

        return _session;
    }

    public async Task<IReadOnlyList<TelemetrySession>> GetSessionsAsync()
    {
        return await _repository.GetAllSessionsAsync();
    }

    public void Play()
    {
        if (_session is null || _session.Readings.Count == 0) return;

        State = PlaybackState.Playing;
        _cts = new CancellationTokenSource();
        _ = PlaybackLoopAsync(_cts.Token);
    }

    public void Pause()
    {
        _cts?.Cancel();
        State = PlaybackState.Paused;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _currentIndex = 0;
        State = PlaybackState.Stopped;
        OnProgress?.Invoke(_currentIndex);
    }

    public void Seek(DateTime timestamp)
    {
        if (_session is null) return;

        _currentIndex = _session.Readings
            .Select((r, i) => new { r, i })
            .Where(x => x.r.Timestamp >= timestamp)
            .Select(x => x.i)
            .FirstOrDefault();

        _logger.LogDebug("Seeked to index {Index}", _currentIndex);
    }

    public void SeekToFraction(double fraction)
    {
        if (_session is null || _session.Readings.Count == 0) return;

        fraction = Math.Clamp(fraction, 0, 1);
        var first = _session.Readings[0].Timestamp;
        var last = _session.Readings[^1].Timestamp;
        var target = first + TimeSpan.FromTicks((long)((last - first).Ticks * fraction));

        Seek(target);
        OnProgress?.Invoke(_currentIndex);
    }

    public void SetSpeed(double multiplier)
    {
        Speed = Math.Clamp(multiplier, 0.25, 8.0);
        _logger.LogDebug("Playback speed set to {Speed}x", Speed);
    }

    private async Task PlaybackLoopAsync(CancellationToken ct)
    {
        if (_session is null) return;

        while (_currentIndex < _session.Readings.Count && !ct.IsCancellationRequested)
        {
            var reading = _session.Readings[_currentIndex];
            OnReading?.Invoke(reading);
            OnProgress?.Invoke(_currentIndex);

            // Calculate delay based on actual time gaps between readings
            if (_currentIndex + 1 < _session.Readings.Count)
            {
                var nextReading = _session.Readings[_currentIndex + 1];
                var gap = nextReading.Timestamp - reading.Timestamp;
                var delay = TimeSpan.FromMilliseconds(gap.TotalMilliseconds / Speed);
                delay = TimeSpan.FromMilliseconds(Math.Max(10, Math.Min(2000, delay.TotalMilliseconds)));

                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            _currentIndex++;
        }

        if (!ct.IsCancellationRequested)
        {
            State = PlaybackState.Stopped;
            _currentIndex = 0;
        }
    }
}
