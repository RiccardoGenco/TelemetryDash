using Microsoft.Extensions.Logging;
using Moq;
using TelemetryDash.Core.Models;
using TelemetryDash.Services;

namespace TelemetryDash.Tests;

public class AnomalyDetectorTests
{
    private readonly AnomalyDetector _detector;

    public AnomalyDetectorTests()
    {
        var logger = Mock.Of<ILogger<AnomalyDetector>>();
        _detector = new AnomalyDetector(logger, windowSize: 20, confidence: 95);
    }

    [Fact]
    public void Should_BeInLearningPhase_Initially()
    {
        Assert.True(_detector.IsLearning);
    }

    [Fact]
    public void Should_ReturnNonAnomaly_DuringLearning()
    {
        var reading = new TelemetryReading
        {
            ChannelId = "TEMP_A1",
            Value = 65.0,
            Timestamp = DateTime.UtcNow
        };

        var result = _detector.Analyze(reading);

        Assert.False(result.IsAnomaly);
        Assert.Equal(1.0, result.PValue);
    }

    [Fact]
    public void Should_ExitLearningPhase_AfterEnoughSamples()
    {
        for (int i = 0; i < 25; i++)
        {
            _detector.Analyze(new TelemetryReading
            {
                ChannelId = "TEMP_A1",
                Value = 65.0 + Math.Sin(i * 0.1) * 2,
                Timestamp = DateTime.UtcNow.AddSeconds(i)
            });
        }

        Assert.False(_detector.IsLearning);
    }

    [Fact]
    public void Should_TrackMultipleChannelsIndependently()
    {
        // Feed channel A past learning
        for (int i = 0; i < 25; i++)
        {
            _detector.Analyze(new TelemetryReading
            {
                ChannelId = "TEMP_A1",
                Value = 65.0,
                Timestamp = DateTime.UtcNow.AddSeconds(i)
            });
        }

        // Channel B should still be learning
        _detector.Analyze(new TelemetryReading
        {
            ChannelId = "PRESS_B2",
            Value = 1013.0,
            Timestamp = DateTime.UtcNow
        });

        // IsLearning should be true because PRESS_B2 is still learning
        Assert.True(_detector.IsLearning);
    }
}
