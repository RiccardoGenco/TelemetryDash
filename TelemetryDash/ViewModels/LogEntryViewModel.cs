using System.Windows.Media;
using TelemetryDash.Core.Mvvm;

namespace TelemetryDash.ViewModels;

public class LogEntryViewModel : ObservableObject
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "INFO";
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public SolidColorBrush LevelBrush => Level switch
    {
        "ERR" or "ERROR" => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        "WRN" or "WARN" => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
        "DBG" or "DEBUG" => new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
        _ => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
    };

    public string TimestampFormatted => Timestamp.ToString("HH:mm:ss.fff");
}
