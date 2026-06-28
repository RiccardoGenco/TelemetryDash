using Microsoft.Extensions.Logging;
using Moq;
using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Models;
using TelemetryDash.Services;

namespace TelemetryDash.Tests;

public class AlarmServiceTests
{
    private readonly AlarmService _service;

    public AlarmServiceTests()
    {
        var logger = Mock.Of<ILogger<AlarmService>>();
        _service = new AlarmService(logger);
    }

    [Fact]
    public void Should_TriggerAlarm_When_ValueExceedsMaxThreshold()
    {
        var config = new AlarmConfig { ChannelId = "TEMP_A1", MaxValue = 85.0, MinValue = 30.0 };
        _service.AddConfig(config);

        var reading = new TelemetryReading { ChannelId = "TEMP_A1", Value = 87.3, Timestamp = DateTime.UtcNow };
        var result = _service.Evaluate(reading);

        Assert.True(result.IsAlarm);
        Assert.Equal(AlarmSeverity.Warning, result.Severity);
    }

    [Fact]
    public void Should_TriggerAlarm_When_ValueBelowMinThreshold()
    {
        var config = new AlarmConfig { ChannelId = "TEMP_A1", MaxValue = 85.0, MinValue = 30.0, Severity = AlarmSeverity.Critical };
        _service.AddConfig(config);

        var reading = new TelemetryReading { ChannelId = "TEMP_A1", Value = 25.0, Timestamp = DateTime.UtcNow };
        var result = _service.Evaluate(reading);

        Assert.True(result.IsAlarm);
        Assert.Equal(AlarmSeverity.Critical, result.Severity);
    }

    [Fact]
    public void Should_NotTriggerAlarm_When_ValueWithinRange()
    {
        var config = new AlarmConfig { ChannelId = "TEMP_A1", MaxValue = 85.0, MinValue = 30.0 };
        _service.AddConfig(config);

        var reading = new TelemetryReading { ChannelId = "TEMP_A1", Value = 60.0, Timestamp = DateTime.UtcNow };
        var result = _service.Evaluate(reading);

        Assert.False(result.IsAlarm);
    }

    [Fact]
    public void Should_NotTriggerAlarm_When_NoConfigForChannel()
    {
        var reading = new TelemetryReading { ChannelId = "UNKNOWN", Value = 999.0, Timestamp = DateTime.UtcNow };
        var result = _service.Evaluate(reading);

        Assert.False(result.IsAlarm);
    }

    [Fact]
    public void Should_TrackActiveAlarms()
    {
        var config = new AlarmConfig { ChannelId = "TEMP_A1", MaxValue = 85.0, MinValue = 30.0 };
        _service.AddConfig(config);

        _service.Evaluate(new TelemetryReading { ChannelId = "TEMP_A1", Value = 90.0, Timestamp = DateTime.UtcNow });
        _service.Evaluate(new TelemetryReading { ChannelId = "TEMP_A1", Value = 95.0, Timestamp = DateTime.UtcNow });

        var alarms = _service.GetActiveAlarms();
        Assert.Equal(2, alarms.Count);
    }

    [Fact]
    public void Should_ClearAlarms()
    {
        var config = new AlarmConfig { ChannelId = "TEMP_A1", MaxValue = 85.0, MinValue = 30.0 };
        _service.AddConfig(config);

        _service.Evaluate(new TelemetryReading { ChannelId = "TEMP_A1", Value = 90.0, Timestamp = DateTime.UtcNow });
        _service.ClearAlarms();

        Assert.Empty(_service.GetActiveAlarms());
    }

    [Fact]
    public void Should_IncludeMessageWithAlarm()
    {
        var config = new AlarmConfig { ChannelId = "PRESS_B2", MaxValue = 1100.0, MinValue = 900.0 };
        _service.AddConfig(config);

        var reading = new TelemetryReading { ChannelId = "PRESS_B2", Value = 1150.5, Timestamp = DateTime.UtcNow };
        var result = _service.Evaluate(reading);

        Assert.True(result.IsAlarm);
        Assert.Contains("PRESS_B2", result.Message);
        Assert.Contains("1150.50", result.Message);
    }

    [Fact]
    public void Constructor_WithConfig_ShouldRegisterConfig()
    {
        var config = new AlarmConfig { ChannelId = "VIB_C3", MaxValue = 1.2, MinValue = 0.0 };
        var logger = Mock.Of<ILogger<AlarmService>>();
        var service = new AlarmService(config, logger);

        var reading = new TelemetryReading { ChannelId = "VIB_C3", Value = 1.5, Timestamp = DateTime.UtcNow };
        var result = service.Evaluate(reading);

        Assert.True(result.IsAlarm);
    }
}
