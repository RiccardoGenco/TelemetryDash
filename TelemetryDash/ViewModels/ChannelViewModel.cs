using System.Collections.ObjectModel;
using System.Windows.Media;
using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Mvvm;

namespace TelemetryDash.ViewModels;

public class ChannelViewModel : ObservableObject
{
    private const int MaxSparklinePoints = 60;

    private string _channelId = string.Empty;
    private double _currentValue;
    private double _minValue;
    private double _maxValue = 100;
    private string _unit = string.Empty;
    private QualityFlag _quality;
    private double _saturationPercent;
    private SolidColorBrush _saturationBrush = new(Colors.Green);

    public string ChannelId
    {
        get => _channelId;
        set => SetProperty(ref _channelId, value);
    }

    public double CurrentValue
    {
        get => _currentValue;
        set
        {
            if (SetProperty(ref _currentValue, value))
            {
                UpdateSaturation();
                SparklineValues.Add(value);
                if (SparklineValues.Count > MaxSparklinePoints)
                    SparklineValues.RemoveAt(0);
            }
        }
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

    public string Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value);
    }

    public QualityFlag Quality
    {
        get => _quality;
        set => SetProperty(ref _quality, value);
    }

    public double SaturationPercent
    {
        get => _saturationPercent;
        set => SetProperty(ref _saturationPercent, value);
    }

    public SolidColorBrush SaturationBrush
    {
        get => _saturationBrush;
        set => SetProperty(ref _saturationBrush, value);
    }

    public ObservableCollection<double> SparklineValues { get; } = new();

    private void UpdateSaturation()
    {
        var range = MaxValue - MinValue;
        if (range <= 0) return;

        SaturationPercent = Math.Clamp((CurrentValue - MinValue) / range * 100, 0, 100);

        SaturationBrush = SaturationPercent switch
        {
            > 90 => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)), // Red
            > 70 => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), // Amber
            _ => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),    // Green
        };
    }
}
