namespace TelemetryDash.Core.Models;

public class AnomalyPrediction
{
    /// <summary>
    /// ML.NET IID Spike Detection output vector:
    /// [0] IsAnomaly (1.0 = true), [1] p-value, [2] ExpectedValue
    /// </summary>
    public double[] Prediction { get; set; } = Array.Empty<double>();

    public bool IsAnomaly => Prediction.Length > 0 && Prediction[0] != 0;
    public double PValue => Prediction.Length > 1 ? Prediction[1] : 1.0;
    public double ExpectedValue => Prediction.Length > 2 ? Prediction[2] : 0.0;
}
