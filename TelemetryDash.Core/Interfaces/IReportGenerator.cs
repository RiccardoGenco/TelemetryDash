using TelemetryDash.Core.Models;

namespace TelemetryDash.Core.Interfaces;

public interface IReportGenerator
{
    Task<string> GenerateAsync(TelemetrySession session, string outputPath);
}
