using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Interfaces;
using TelemetryDash.Core.Models;

namespace TelemetryDash.Services;

public class AlarmService : IAlarmService
{
    private readonly ILogger<AlarmService> _logger;
    private readonly ConcurrentDictionary<string, AlarmConfig> _configs = new();
    private readonly ConcurrentBag<AlarmResult> _activeAlarms = new();

    public AlarmService(ILogger<AlarmService> logger)
    {
        _logger = logger;
    }

    public AlarmService(AlarmConfig config, ILogger<AlarmService> logger)
    {
        _logger = logger;
        _configs[config.ChannelId] = config;
    }

    public void AddConfig(AlarmConfig config)
    {
        _configs[config.ChannelId] = config;
        _logger.LogInformation("Alarm config added for {Channel}: Min={Min}, Max={Max}",
            config.ChannelId, config.MinValue, config.MaxValue);
    }

    public IReadOnlyCollection<AlarmConfig> GetConfigs()
    {
        return _configs.Values.ToList().AsReadOnly();
    }

    public AlarmResult Evaluate(TelemetryReading reading)
    {
        if (!_configs.TryGetValue(reading.ChannelId, out var config))
        {
            return new AlarmResult { IsAlarm = false, Reading = reading };
        }

        AlarmResult result;

        if (reading.Value > config.MaxValue)
        {
            result = new AlarmResult
            {
                IsAlarm = true,
                Severity = config.Severity,
                Timestamp = reading.Timestamp,
                Reading = reading,
                Message = string.Format(CultureInfo.InvariantCulture, "{0}: value {1:F2} exceeds max threshold {2:F2}", reading.ChannelId, reading.Value, config.MaxValue)
            };
        }
        else if (reading.Value < config.MinValue)
        {
            result = new AlarmResult
            {
                IsAlarm = true,
                Severity = config.Severity,
                Timestamp = reading.Timestamp,
                Reading = reading,
                Message = string.Format(CultureInfo.InvariantCulture, "{0}: value {1:F2} below min threshold {2:F2}", reading.ChannelId, reading.Value, config.MinValue)
            };
        }
        else
        {
            return new AlarmResult { IsAlarm = false, Reading = reading };
        }

        _activeAlarms.Add(result);
        _logger.LogWarning("Alarm triggered: {Message}", result.Message);
        return result;
    }

    public IReadOnlyList<AlarmResult> GetActiveAlarms()
    {
        return _activeAlarms.ToList().AsReadOnly();
    }

    public void ClearAlarms()
    {
        while (_activeAlarms.TryTake(out _)) { }
    }
}
