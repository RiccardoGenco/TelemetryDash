using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Mvvm;

namespace TelemetryDash.ViewModels;

public class ChannelViewModel : ObservableObject
{
    private const int MaxSparklinePoints = 60;

    // Theme colours (kept in sync with Resources/Theme.xaml)
    private static readonly Color GreenColor = Color.FromRgb(0x38, 0xF0, 0x6A);
    private static readonly Color AmberColor = Color.FromRgb(0xFF, 0xB0, 0x00);
    private static readonly Color RedColor = Color.FromRgb(0xFF, 0x55, 0x55);
    private static readonly Color BorderColor = Color.FromRgb(0x1E, 0x4A, 0x2C);
    private static readonly Color StaleColor = Color.FromRgb(0x55, 0x66, 0x55);

    private string _channelId = string.Empty;
    private double _currentValue;
    private double _minValue;
    private double _maxValue = 100;
    private string _unit = string.Empty;
    private QualityFlag _quality;
    private double _saturationPercent;
    private SolidColorBrush _saturationBrush = new(Colors.Green);
    private AlarmSeverity? _alarmState;
    private bool _isStale;
    private DateTime _lastUpdateUtc = DateTime.UtcNow;

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
                LastUpdateUtc = DateTime.UtcNow;
                IsStale = false;
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
        set
        {
            if (SetProperty(ref _quality, value))
            {
                OnPropertyChanged(nameof(QualityLabel));
                OnPropertyChanged(nameof(QualityBrush));
            }
        }
    }

    public string QualityLabel => _quality.ToString().ToUpperInvariant();

    public SolidColorBrush QualityBrush => _quality switch
    {
        QualityFlag.Error => new SolidColorBrush(RedColor),
        QualityFlag.Warning => new SolidColorBrush(AmberColor),
        _ => new SolidColorBrush(GreenColor),
    };

    /// <summary>Current alarm severity for this channel, or null if within thresholds.</summary>
    public AlarmSeverity? AlarmState
    {
        get => _alarmState;
        set
        {
            if (SetProperty(ref _alarmState, value))
            {
                OnPropertyChanged(nameof(IsInAlarm));
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(CardBorderThickness));
            }
        }
    }

    public bool IsInAlarm => _alarmState is not null;

    /// <summary>True when the channel has not produced a reading recently.</summary>
    public bool IsStale
    {
        get => _isStale;
        set
        {
            if (SetProperty(ref _isStale, value))
            {
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(CardBorderThickness));
            }
        }
    }

    public DateTime LastUpdateUtc
    {
        get => _lastUpdateUtc;
        set => SetProperty(ref _lastUpdateUtc, value);
    }

    /// <summary>Border colour reflecting the channel's health at a glance.</summary>
    public SolidColorBrush CardBorderBrush
    {
        get
        {
            if (_isStale) return new SolidColorBrush(StaleColor);
            return _alarmState switch
            {
                AlarmSeverity.Critical => new SolidColorBrush(RedColor),
                AlarmSeverity.Warning => new SolidColorBrush(AmberColor),
                AlarmSeverity.Info => new SolidColorBrush(AmberColor),
                _ => new SolidColorBrush(BorderColor),
            };
        }
    }

    public Thickness CardBorderThickness => new((_isStale || _alarmState is not null) ? 2 : 1);

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
