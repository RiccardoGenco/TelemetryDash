using System.IO;
using TelemetryDash.Core.Interfaces;

namespace TelemetryDash.Services;

public class FileLogger : IFileLogger, IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    public FileLogger(string logDirectory = "logs")
    {
        Directory.CreateDirectory(logDirectory);
        var filename = $"events_{DateTime.Now:yyyyMMdd}.csv";
        var path = Path.Combine(logDirectory, filename);
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    public void Log(string level, string source, string message)
    {
        if (_disposed) return;
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff};{level};{source};{message}";
        _writer.WriteLine(line);
    }

    public void Flush()
    {
        if (_disposed) return;
        _writer.Flush();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Flush();
        _writer.Dispose();
    }
}
