using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Interfaces;
using TelemetryDash.Core.Models;

namespace TelemetryDash.Infrastructure.Plugins;

// Exported via [InheritedExport] on IDataSourcePlugin — no explicit [Export] to avoid duplicate registration.
public class TelemetrySimulator : IDataSourcePlugin
{
    private readonly ILogger<TelemetrySimulator> _logger;
    private readonly Random _random = new();
    private bool _isConnected;

    public string Name => "Simulator";

    private static readonly ChannelSpec[] Channels =
    {
        new("TEMP_A1", BaseValue: 65.0, Amplitude: 15.0, NoiseLevel: 2.0, SpikeChance: 0.02),
        new("PRESS_B2", BaseValue: 1013.0, Amplitude: 50.0, NoiseLevel: 5.0, SpikeChance: 0.015),
        new("VIB_C3", BaseValue: 0.5, Amplitude: 0.3, NoiseLevel: 0.05, SpikeChance: 0.03),
        new("FLOW_D4", BaseValue: 120.0, Amplitude: 20.0, NoiseLevel: 3.0, SpikeChance: 0.01),
    };

    public TelemetrySimulator()
    {
        _logger = NullLogger<TelemetrySimulator>.Instance;
    }

    public TelemetrySimulator(ILogger<TelemetrySimulator> logger)
    {
        _logger = logger;
    }

    public Task ConnectAsync()
    {
        _isConnected = true;
        _logger.LogInformation("Simulator connected");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _isConnected = false;
        _logger.LogInformation("Simulator disconnected");
        return Task.CompletedTask;
    }

    public IObservable<TelemetryReading> GetDataStream(CancellationToken ct)
    {
        return Observable.Create<TelemetryReading>(observer =>
        {
            return Observable.Interval(TimeSpan.FromMilliseconds(500))
                .TakeWhile(_ => _isConnected && !ct.IsCancellationRequested)
                .Subscribe(_ =>
                {
                    var timestamp = DateTime.UtcNow;
                    var elapsed = timestamp.TimeOfDay.TotalSeconds;

                    foreach (var channel in Channels)
                    {
                        var value = GenerateValue(channel, elapsed);
                        var quality = DetermineQuality(channel, value);

                        observer.OnNext(new TelemetryReading
                        {
                            ChannelId = channel.Id,
                            Value = Math.Round(value, 3),
                            Timestamp = timestamp,
                            Quality = quality,
                        });
                    }
                },
                observer.OnError,
                observer.OnCompleted);
        });
    }

    private double GenerateValue(ChannelSpec spec, double elapsedSeconds)
    {
        // Sinusoidal base pattern with different frequencies per channel
        var frequency = spec.Id switch
        {
            "TEMP_A1" => 0.05,
            "PRESS_B2" => 0.03,
            "VIB_C3" => 0.15,
            "FLOW_D4" => 0.08,
            _ => 0.05
        };

        var sineComponent = spec.Amplitude * Math.Sin(2 * Math.PI * frequency * elapsedSeconds);
        var noise = ((_random.NextDouble() * 2) - 1) * spec.NoiseLevel;
        var value = spec.BaseValue + sineComponent + noise;

        // Occasional spike
        if (_random.NextDouble() < spec.SpikeChance)
        {
            var spikeDirection = _random.NextDouble() > 0.5 ? 1 : -1;
            value += spikeDirection * spec.Amplitude * 1.5;
            _logger.LogDebug("Spike generated on {Channel}", spec.Id);
        }

        return value;
    }

    private QualityFlag DetermineQuality(ChannelSpec spec, double value)
    {
        var deviation = Math.Abs(value - spec.BaseValue) / spec.Amplitude;
        return deviation switch
        {
            > 1.5 => QualityFlag.Error,
            > 1.2 => QualityFlag.Warning,
            _ => QualityFlag.Ok
        };
    }

    private record ChannelSpec(string Id, double BaseValue, double Amplitude, double NoiseLevel, double SpikeChance);
}
