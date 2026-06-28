using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using TelemetryDash.Core.Interfaces;
using TelemetryDash.Core.Models;

namespace TelemetryDash.Services;

public class AnomalyDetector : IAnomalyDetector
{
    private readonly ILogger<AnomalyDetector> _logger;
    private readonly MLContext _mlContext;
    private readonly int _windowSize;
    private readonly double _confidence;
    private readonly Dictionary<string, ChannelDetector> _detectors = new();

    public bool IsLearning { get; private set; } = true;

    public AnomalyDetector(ILogger<AnomalyDetector> logger, int windowSize = 120, double confidence = 95)
    {
        _logger = logger;
        _mlContext = new MLContext(seed: 0);
        _windowSize = windowSize;
        _confidence = confidence;
    }

    public AnomalyPrediction Analyze(TelemetryReading reading)
    {
        if (!_detectors.TryGetValue(reading.ChannelId, out var detector))
        {
            detector = new ChannelDetector(_windowSize);
            _detectors[reading.ChannelId] = detector;
        }

        detector.AddReading(reading.Value);

        // Still in learning phase - need enough samples
        if (detector.Count < _windowSize)
        {
            IsLearning = _detectors.Values.Any(d => d.Count < _windowSize);
            return new AnomalyPrediction { Prediction = new double[] { 0, 1.0, reading.Value } };
        }

        IsLearning = _detectors.Values.Any(d => d.Count < _windowSize);

        // Run IID Spike Detection
        try
        {
            var dataView = _mlContext.Data.LoadFromEnumerable(
                detector.GetReadings().Select(v => new SpikeInput { Value = (float)v }));

            var pipeline = _mlContext.Transforms.DetectIidSpike(
                outputColumnName: nameof(SpikePrediction.Prediction),
                inputColumnName: nameof(SpikeInput.Value),
                confidence: _confidence,
                pvalueHistoryLength: _windowSize / 4);

            var model = pipeline.Fit(dataView);
            var transformedData = model.Transform(dataView);
            var predictions = _mlContext.Data.CreateEnumerable<SpikePrediction>(transformedData, reuseRowObject: false)
                .ToList();

            // Return the prediction for the last (most recent) reading
            if (predictions.Count > 0)
            {
                var last = predictions[^1];
                var result = new AnomalyPrediction
                {
                    Prediction = last.Prediction.Select(v => (double)v).ToArray()
                };

                if (result.IsAnomaly)
                {
                    _logger.LogWarning("Anomaly detected on {Channel}: p-value={PValue:F4}, expected={Expected:F2}, actual={Actual:F2}",
                        reading.ChannelId, result.PValue, result.ExpectedValue, reading.Value);
                }

                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anomaly detection failed for {Channel}", reading.ChannelId);
        }

        return new AnomalyPrediction { Prediction = new double[] { 0, 1.0, reading.Value } };
    }

    private class SpikeInput
    {
        public float Value { get; set; }
    }

    private class SpikePrediction
    {
        [VectorType(3)]
        public double[] Prediction { get; set; } = Array.Empty<double>();
    }

    private class ChannelDetector
    {
        private readonly Queue<double> _values;
        private readonly int _maxSize;

        public int Count => _values.Count;

        public ChannelDetector(int maxSize)
        {
            _maxSize = maxSize * 2; // Keep 2x window for context
            _values = new Queue<double>(_maxSize);
        }

        public void AddReading(double value)
        {
            _values.Enqueue(value);
            if (_values.Count > _maxSize)
                _values.Dequeue();
        }

        public IReadOnlyList<double> GetReadings() => _values.ToList();
    }
}
