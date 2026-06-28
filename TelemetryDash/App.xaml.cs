using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TelemetryDash.Core.Interfaces;
using TelemetryDash.Infrastructure.Data;
using TelemetryDash.Infrastructure.Plugins;
using TelemetryDash.Services;

namespace TelemetryDash;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public static ServiceProvider Services { get; private set; } = null!;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: "logs/telemetry-.csv",
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff};{Level:u3};{SourceContext};{Message:lj}{NewLine}")
            .CreateLogger();

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(serilogLogger, dispose: true);
        });

        // Core services
        services.AddSingleton<IAlarmService, AlarmService>();
        services.AddSingleton<IAnomalyDetector, AnomalyDetector>();
        services.AddSingleton<IFileLogger, FileLogger>();
        services.AddSingleton<PluginLoader>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<IReportGenerator, ReportGenerator>();
        services.AddSingleton<IPlaybackService, PlaybackService>();

        _serviceProvider = services.BuildServiceProvider();
        Services = _serviceProvider;

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
