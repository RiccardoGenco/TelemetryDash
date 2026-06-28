namespace TelemetryDash.Core.Interfaces;

public interface IFileLogger
{
    void Log(string level, string source, string message);
    void Flush();
}
