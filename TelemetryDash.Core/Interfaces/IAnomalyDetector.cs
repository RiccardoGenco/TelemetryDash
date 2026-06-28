using TelemetryDash.Core.Models;

namespace TelemetryDash.Core.Interfaces;

public interface IAnomalyDetector
{
    AnomalyPrediction Analyze(TelemetryReading reading);
    bool IsLearning { get; }
}
