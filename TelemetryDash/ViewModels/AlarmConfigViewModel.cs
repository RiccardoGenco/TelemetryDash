using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Models;
using TelemetryDash.Core.Mvvm;

namespace TelemetryDash.ViewModels;

/// <summary>Editable row in the Settings panel for one channel's alarm thresholds.</summary>
public class AlarmConfigViewModel : ObservableObject
{
    private string _channelId = string.Empty;
    private double _minValue;
    private double _maxValue;
    private AlarmSeverity _severity = AlarmSeverity.Warning;

    public string ChannelId
    {
        get => _channelId;
        set => SetProperty(ref _channelId, value);
    }

    public double MinValue
    {
        get => _minValue;
        set => SetProperty(ref _minValue, value);
    }

    public double MaxValue
    {
        get => _maxValue;
        set => SetProperty(ref _maxValue, value);
    }

    public AlarmSeverity Severity
    {
        get => _severity;
        set => SetProperty(ref _severity, value);
    }

    public IReadOnlyList<AlarmSeverity> SeverityOptions { get; } =
        Enum.GetValues<AlarmSeverity>();

    public AlarmConfigViewModel() { }

    public AlarmConfigViewModel(AlarmConfig config)
    {
        _channelId = config.ChannelId;
        _minValue = config.MinValue;
        _maxValue = config.MaxValue;
        _severity = config.Severity;
    }

    public AlarmConfig ToConfig() => new()
    {
        ChannelId = ChannelId,
        MinValue = MinValue,
        MaxValue = MaxValue,
        Severity = Severity,
    };
}
