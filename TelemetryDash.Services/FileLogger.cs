using System.IO;
using TelemetryDash.Core.Interfaces;

namespace TelemetryDash.Services;

public class FileLogger : IFileLogger
{
    private readonly StreamWriter _writer;

    public FileLogger(string logDirectory = "logs")
    {
        Directory.CreateDirectory(logDirectory);
        var filename = $"events_{DateTime.Now:yyyyMMdd}.csv";
        var path = Path.Combine(logDirectory, filename);
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    public void Log(string level, string source, string message)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff};{level};{source};{message}";
        _writer.WriteLine(line);
    }

    public void Flush()
    {
        _writer.Flush();
    }
}
