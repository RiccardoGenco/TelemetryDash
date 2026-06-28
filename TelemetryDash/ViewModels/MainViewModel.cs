using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Interfaces;
using TelemetryDash.Core.Models;
using TelemetryDash.Core.Mvvm;
using TelemetryDash.Infrastructure.Plugins;
using TelemetryDash.Services;

namespace TelemetryDash.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly PluginLoader _pluginLoader;
    private readonly IAlarmService _alarmService;
    private readonly IAnomalyDetector _anomalyDetector;
    private IDataSourcePlugin? _activePlugin;
    private IDisposable? _dataSubscription;
    private CancellationTokenSource? _cts;

    private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
    private string _lastUpdateTimestamp = "--:--:--";
    private bool _isLearning;
    private string _selectedLanguage = "EN";

    public ConnectionStatus ConnectionStatus
    {
        get => _connectionStatus;
        set
        {
            if (SetProperty(ref _connectionStatus, value))
                OnPropertyChanged(nameof(IsConnected));
        }
    }

    public bool IsConnected => ConnectionStatus == ConnectionStatus.Connected;

    public string LastUpdateTimestamp
    {
        get => _lastUpdateTimestamp;
        set => SetProperty(ref _lastUpdateTimestamp, value);
    }

    public bool IsLearning
    {
        get => _isLearning;
        set => SetProperty(ref _isLearning, value);
    }

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value))
                SwitchLanguage(value);
        }
    }

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ObservableCollection<AlarmViewModel> Alarms { get; } = new();
    public ObservableCollection<LogEntryViewModel> LogEntries { get; } = new();
    public ObservableCollection<string> AvailablePlugins { get; } = new();
    public ObservableCollection<string> Languages { get; } = new() { "EN", "DE" };

    private string _selectedPlugin = string.Empty;
    public string SelectedPlugin
    {
        get => _selectedPlugin;
        set => SetProperty(ref _selectedPlugin, value);
    }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand GenerateReportCommand { get; }

    public MainViewModel()
    {
        _logger = App.Services.GetRequiredService<ILogger<MainViewModel>>();
        _alarmService = App.Services.GetRequiredService<IAlarmService>();
        _anomalyDetector = App.Services.GetRequiredService<IAnomalyDetector>();
        _pluginLoader = App.Services.GetRequiredService<PluginLoader>();

        ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !IsConnected);
        DisconnectCommand = new RelayCommand(async () => await DisconnectAsync(), () => IsConnected);
        GenerateReportCommand = new RelayCommand(() => { /* Implemented in Fase 7 */ });

        InitializePlugins();
        InitializeAlarmConfigs();
    }

    private void InitializePlugins()
    {
        _pluginLoader.LoadPlugins("Plugins");
        foreach (var plugin in _pluginLoader.Plugins)
        {
            AvailablePlugins.Add(plugin.Name);
        }

        if (AvailablePlugins.Count > 0)
            SelectedPlugin = AvailablePlugins[0];

        AddLog("INFO", "System", $"Loaded {AvailablePlugins.Count} plugin(s)");
    }

    private void InitializeAlarmConfigs()
    {
        _alarmService.AddConfig(new AlarmConfig { ChannelId = "TEMP_A1", MinValue = 30.0, MaxValue = 85.0, Severity = AlarmSeverity.Warning });
        _alarmService.AddConfig(new AlarmConfig { ChannelId = "PRESS_B2", MinValue = 900.0, MaxValue = 1100.0, Severity = AlarmSeverity.Critical });
        _alarmService.AddConfig(new AlarmConfig { ChannelId = "VIB_C3", MinValue = 0.0, MaxValue = 1.2, Severity = AlarmSeverity.Warning });
        _alarmService.AddConfig(new AlarmConfig { ChannelId = "FLOW_D4", MinValue = 80.0, MaxValue = 160.0, Severity = AlarmSeverity.Warning });
    }

    private async Task ConnectAsync()
    {
        _activePlugin = _pluginLoader.GetPlugin(SelectedPlugin);
        if (_activePlugin is null)
        {
            AddLog("ERR", "Connection", $"Plugin '{SelectedPlugin}' not found");
            return;
        }

        try
        {
            ConnectionStatus = ConnectionStatus.Connecting;
            AddLog("INFO", "Connection", $"Connecting to {_activePlugin.Name}...");

            await _activePlugin.ConnectAsync();
            ConnectionStatus = ConnectionStatus.Connected;
            AddLog("INFO", "Connection", "Connected");

            _cts = new CancellationTokenSource();
            _dataSubscription = _activePlugin.GetDataStream(_cts.Token)
                .ObserveOn(SynchronizationContext.Current!)
                .Subscribe(OnReadingReceived, OnError, OnCompleted);
        }
        catch (Exception ex)
        {
            ConnectionStatus = ConnectionStatus.Error;
            AddLog("ERR", "Connection", ex.Message);
        }
    }

    private async Task DisconnectAsync()
    {
        if (_activePlugin is null) return;

        _cts?.Cancel();
        _dataSubscription?.Dispose();

        try
        {
            await _activePlugin.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect");
        }

        ConnectionStatus = ConnectionStatus.Disconnected;
        AddLog("INFO", "Connection", "Disconnected");
    }

    private void OnReadingReceived(TelemetryReading reading)
    {
        LastUpdateTimestamp = reading.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");

        // Update or create channel view model
        var channel = Channels.FirstOrDefault(c => c.ChannelId == reading.ChannelId);
        if (channel is null)
        {
            channel = new ChannelViewModel
            {
                ChannelId = reading.ChannelId,
                Unit = GetUnit(reading.ChannelId),
                MinValue = GetMinRange(reading.ChannelId),
                MaxValue = GetMaxRange(reading.ChannelId),
            };
            Channels.Add(channel);
        }

        channel.CurrentValue = reading.Value;
        channel.Quality = reading.Quality;

        // Evaluate alarms
        var alarmResult = _alarmService.Evaluate(reading);
        if (alarmResult.IsAlarm)
        {
            Alarms.Insert(0, new AlarmViewModel
            {
                Timestamp = alarmResult.Timestamp,
                ChannelId = alarmResult.Reading.ChannelId,
                Severity = alarmResult.Severity,
                Message = alarmResult.Message,
            });

            // Keep last 100 alarms in UI
            while (Alarms.Count > 100)
                Alarms.RemoveAt(Alarms.Count - 1);
        }

        // Anomaly detection
        var anomaly = _anomalyDetector.Analyze(reading);
        IsLearning = _anomalyDetector.IsLearning;

        if (anomaly.IsAnomaly)
        {
            AddLog("WRN", "Anomaly", $"{reading.ChannelId}: anomaly detected (p={anomaly.PValue:F4})");
        }
    }

    private void OnError(Exception ex)
    {
        ConnectionStatus = ConnectionStatus.Error;
        AddLog("ERR", "DataStream", ex.Message);
    }

    private void OnCompleted()
    {
        ConnectionStatus = ConnectionStatus.Disconnected;
        AddLog("INFO", "DataStream", "Stream completed");
    }

    private void AddLog(string level, string source, string message)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            LogEntries.Insert(0, new LogEntryViewModel
            {
                Timestamp = DateTime.Now,
                Level = level,
                Source = source,
                Message = message,
            });

            while (LogEntries.Count > 500)
                LogEntries.RemoveAt(LogEntries.Count - 1);
        });
    }

    private static void SwitchLanguage(string lang)
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{lang.ToLower()}.xaml", UriKind.Relative)
        };

        var app = Application.Current;
        // Replace only the strings dictionary (index 0)
        if (app.Resources.MergedDictionaries.Count > 0)
            app.Resources.MergedDictionaries[0] = dict;
    }

    private static string GetUnit(string channelId) => channelId switch
    {
        "TEMP_A1" => "\u00b0C",
        "PRESS_B2" => "hPa",
        "VIB_C3" => "g",
        "FLOW_D4" => "L/min",
        _ => ""
    };

    private static double GetMinRange(string channelId) => channelId switch
    {
        "TEMP_A1" => 30.0,
        "PRESS_B2" => 900.0,
        "VIB_C3" => 0.0,
        "FLOW_D4" => 80.0,
        _ => 0
    };

    private static double GetMaxRange(string channelId) => channelId switch
    {
        "TEMP_A1" => 100.0,
        "PRESS_B2" => 1100.0,
        "VIB_C3" => 1.5,
        "FLOW_D4" => 170.0,
        _ => 100
    };
}
