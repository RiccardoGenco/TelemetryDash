using System.Windows.Media;
using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Mvvm;

namespace TelemetryDash.ViewModels;

public class AlarmViewModel : ObservableObject
{
    public DateTime Timestamp { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public AlarmSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;

    public SolidColorBrush SeverityBrush => Severity switch
    {
        AlarmSeverity.Critical => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        AlarmSeverity.Warning => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
        _ => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
    };

    public string SeverityLabel => Severity.ToString().ToUpperInvariant();
    public string TimestampFormatted => Timestamp.ToString("HH:mm:ss.fff");
}
