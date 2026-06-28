using System.Collections.ObjectModel;
using System.IO;
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
    private readonly ISessionRepository _sessionRepository;
    private readonly IReportGenerator _reportGenerator;
    private readonly IFileLogger _fileLogger;
    private readonly IPlaybackService _playbackService;
    private IDataSourcePlugin? _activePlugin;
    private IDisposable? _dataSubscription;
    private CancellationTokenSource? _cts;

    private Guid? _currentSessionId;
    private readonly List<TelemetryReading> _readingBuffer = new();
    private readonly object _bufferLock = new();
    private const int ReadingBufferSize = 20;

    private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
    private string _lastUpdateTimestamp = "--:--:--";
    private bool _isLearning;
    private string _selectedLanguage = "EN";
    private bool _isPlaybackMode;

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

    public bool IsPlaybackMode
    {
        get => _isPlaybackMode;
        set
        {
            if (SetProperty(ref _isPlaybackMode, value))
            {
                OnPropertyChanged(nameof(IsLiveMode));
                OnPropertyChanged(nameof(PlaybackVisibility));
            }
        }
    }

    public bool IsLiveMode => !IsPlaybackMode;
    public Visibility PlaybackVisibility => IsPlaybackMode ? Visibility.Visible : Visibility.Collapsed;

    private PlaybackState _playbackState = PlaybackState.Stopped;
    public PlaybackState PlaybackState
    {
        get => _playbackState;
        set => SetProperty(ref _playbackState, value);
    }

    private double _playbackSpeed = 1.0;
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (SetProperty(ref _playbackSpeed, value))
                _playbackService.SetSpeed(value);
        }
    }

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ObservableCollection<AlarmViewModel> Alarms { get; } = new();
    public ObservableCollection<LogEntryViewModel> LogEntries { get; } = new();
    public ObservableCollection<string> AvailablePlugins { get; } = new();
    public ObservableCollection<string> Languages { get; } = new() { "EN", "DE" };
    public ObservableCollection<SessionItemViewModel> AvailableSessions { get; } = new();

    private string _selectedPlugin = string.Empty;
    public string SelectedPlugin
    {
        get => _selectedPlugin;
        set => SetProperty(ref _selectedPlugin, value);
    }

    private SessionItemViewModel? _selectedSession;
    public SessionItemViewModel? SelectedSession
    {
        get => _selectedSession;
        set => SetProperty(ref _selectedSession, value);
    }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand GenerateReportCommand { get; }
    public ICommand TogglePlaybackCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }

    public MainViewModel()
    {
        _logger = App.Services.GetRequiredService<ILogger<MainViewModel>>();
        _alarmService = App.Services.GetRequiredService<IAlarmService>();
        _anomalyDetector = App.Services.GetRequiredService<IAnomalyDetector>();
        _pluginLoader = App.Services.GetRequiredService<PluginLoader>();
        _sessionRepository = App.Services.GetRequiredService<ISessionRepository>();
        _reportGenerator = App.Services.GetRequiredService<IReportGenerator>();
        _fileLogger = App.Services.GetRequiredService<IFileLogger>();
        _playbackService = App.Services.GetRequiredService<IPlaybackService>();

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && IsLiveMode);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
        GenerateReportCommand = new AsyncRelayCommand(GenerateReportAsync);
        TogglePlaybackCommand = new AsyncRelayCommand(TogglePlaybackModeAsync, () => !IsConnected);
        PlayCommand = new AsyncRelayCommand(PlaySessionAsync, () => IsPlaybackMode && PlaybackState != PlaybackState.Playing);
        PauseCommand = new RelayCommand(() => _playbackService.Pause(), () => PlaybackState == PlaybackState.Playing);
        StopCommand = new RelayCommand(() => _playbackService.Stop(), () => PlaybackState != PlaybackState.Stopped);

        _playbackService.OnReading += OnPlaybackReading;
        _playbackService.OnStateChanged += OnPlaybackStateChanged;

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
            _fileLogger.Log("INFO", "Connection", $"Connected to {_activePlugin.Name}");

            // Create a new session in the database
            var session = new TelemetrySession
            {
                StartTime = DateTime.UtcNow,
                DataSourceName = _activePlugin.Name,
            };
            _currentSessionId = await _sessionRepository.CreateSessionAsync(session);
            AddLog("INFO", "Session", $"Session {_currentSessionId} started");

            _cts = new CancellationTokenSource();
            _dataSubscription = _activePlugin.GetDataStream(_cts.Token)
                .ObserveOn(SynchronizationContext.Current!)
                .Subscribe(OnLiveReadingReceived, OnError, OnCompleted);
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
            // Flush remaining readings buffer
            await FlushReadingBufferAsync();

            // End the session
            if (_currentSessionId.HasValue)
            {
                await _sessionRepository.EndSessionAsync(_currentSessionId.Value, DateTime.UtcNow);
                AddLog("INFO", "Session", $"Session {_currentSessionId} ended");
                _fileLogger.Log("INFO", "Session", $"Session {_currentSessionId} ended");
            }

            await _activePlugin.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect");
        }

        _currentSessionId = null;
        ConnectionStatus = ConnectionStatus.Disconnected;
        AddLog("INFO", "Connection", "Disconnected");
    }

    /// <summary>
    /// Handles readings from the live data stream — updates UI and persists to DB.
    /// </summary>
    private void OnLiveReadingReceived(TelemetryReading reading)
    {
        UpdateUI(reading);

        // Buffer readings for batch persistence
        if (_currentSessionId.HasValue)
        {
            List<TelemetryReading>? batch = null;
            lock (_bufferLock)
            {
                _readingBuffer.Add(reading);
                if (_readingBuffer.Count >= ReadingBufferSize)
                {
                    batch = new List<TelemetryReading>(_readingBuffer);
                    _readingBuffer.Clear();
                }
            }

            if (batch is not null)
            {
                _ = PersistBatchAsync(batch);
            }

            // Persist alarms
            var alarmResult = _alarmService.Evaluate(reading);
            if (alarmResult.IsAlarm)
            {
                AddAlarmToUI(alarmResult);
                _ = _sessionRepository.SaveAlarmAsync(_currentSessionId.Value, alarmResult);
                _fileLogger.Log("WRN", "Alarm", alarmResult.Message);
            }
        }
        else
        {
            // No session — still evaluate alarms for UI display
            var alarmResult = _alarmService.Evaluate(reading);
            if (alarmResult.IsAlarm)
                AddAlarmToUI(alarmResult);
        }

        // Anomaly detection
        var anomaly = _anomalyDetector.Analyze(reading);
        IsLearning = _anomalyDetector.IsLearning;

        if (anomaly.IsAnomaly)
        {
            var msg = $"{reading.ChannelId}: anomaly detected (p={anomaly.PValue:F4})";
            AddLog("WRN", "Anomaly", msg);
            _fileLogger.Log("WRN", "Anomaly", msg);
        }
    }

    /// <summary>
    /// Handles readings from the playback service — updates UI only, no persistence.
    /// </summary>
    private void OnPlaybackReading(TelemetryReading reading)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            UpdateUI(reading);

            var alarmResult = _alarmService.Evaluate(reading);
            if (alarmResult.IsAlarm)
                AddAlarmToUI(alarmResult);
        });
    }

    private void OnPlaybackStateChanged(PlaybackState state)
    {
        Application.Current?.Dispatcher.Invoke(() => PlaybackState = state);
    }

    /// <summary>
    /// Shared UI update logic for both live and playback readings.
    /// </summary>
    private void UpdateUI(TelemetryReading reading)
    {
        LastUpdateTimestamp = reading.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");

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
    }

    private void AddAlarmToUI(AlarmResult alarmResult)
    {
        Alarms.Insert(0, new AlarmViewModel
        {
            Timestamp = alarmResult.Timestamp,
            ChannelId = alarmResult.Reading.ChannelId,
            Severity = alarmResult.Severity,
            Message = alarmResult.Message,
        });

        while (Alarms.Count > 100)
            Alarms.RemoveAt(Alarms.Count - 1);
    }

    private async Task PersistBatchAsync(List<TelemetryReading> batch)
    {
        if (!_currentSessionId.HasValue) return;

        try
        {
            await _sessionRepository.SaveReadingsAsync(_currentSessionId.Value, batch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist readings batch");
        }
    }

    private async Task FlushReadingBufferAsync()
    {
        List<TelemetryReading> batch;
        lock (_bufferLock)
        {
            if (_readingBuffer.Count == 0) return;
            batch = new List<TelemetryReading>(_readingBuffer);
            _readingBuffer.Clear();
        }

        await PersistBatchAsync(batch);
    }

    private void OnError(Exception ex)
    {
        ConnectionStatus = ConnectionStatus.Error;
        AddLog("ERR", "DataStream", ex.Message);
        _fileLogger.Log("ERR", "DataStream", ex.Message);
    }

    private void OnCompleted()
    {
        ConnectionStatus = ConnectionStatus.Disconnected;
        AddLog("INFO", "DataStream", "Stream completed");
    }

    private async Task GenerateReportAsync()
    {
        Guid sessionId;

        if (IsPlaybackMode && SelectedSession is not null)
        {
            sessionId = SelectedSession.Id;
        }
        else if (_currentSessionId.HasValue)
        {
            await FlushReadingBufferAsync();
            sessionId = _currentSessionId.Value;
        }
        else
        {
            AddLog("WRN", "Report", "No session available for report generation");
            return;
        }

        try
        {
            AddLog("INFO", "Report", "Generating report...");
            var session = await _sessionRepository.GetSessionAsync(sessionId);
            if (session is null || session.Readings.Count == 0)
            {
                AddLog("WRN", "Report", "Session has no data to report");
                return;
            }

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TelemetryDash");
            Directory.CreateDirectory(dir);
            var outputPath = Path.Combine(dir, $"Report_{sessionId:N}.pdf");

            await _reportGenerator.GenerateAsync(session, outputPath);
            AddLog("INFO", "Report", $"Report saved to {outputPath}");
            _fileLogger.Log("INFO", "Report", $"Generated: {outputPath}");
        }
        catch (Exception ex)
        {
            AddLog("ERR", "Report", $"Report generation failed: {ex.Message}");
            _logger.LogError(ex, "Report generation failed");
        }
    }

    private async Task TogglePlaybackModeAsync()
    {
        if (!IsPlaybackMode)
        {
            Channels.Clear();
            Alarms.Clear();

            var sessions = await _playbackService.GetSessionsAsync();
            AvailableSessions.Clear();
            foreach (var s in sessions)
            {
                AvailableSessions.Add(new SessionItemViewModel
                {
                    Id = s.Id,
                    Label = $"{s.DataSourceName} - {s.StartTime:yyyy-MM-dd HH:mm:ss}",
                });
            }

            if (AvailableSessions.Count > 0)
                SelectedSession = AvailableSessions[0];

            IsPlaybackMode = true;
            AddLog("INFO", "Playback", $"Playback mode - {AvailableSessions.Count} session(s) available");
        }
        else
        {
            _playbackService.Stop();
            IsPlaybackMode = false;
            Channels.Clear();
            Alarms.Clear();
            AddLog("INFO", "Playback", "Returned to live mode");
        }
    }

    private async Task PlaySessionAsync()
    {
        if (SelectedSession is null) return;

        try
        {
            Channels.Clear();
            Alarms.Clear();
            _alarmService.ClearAlarms();

            await _playbackService.LoadSessionAsync(SelectedSession.Id);
            _playbackService.Play();
            AddLog("INFO", "Playback", $"Playing session {SelectedSession.Id}");
        }
        catch (Exception ex)
        {
            AddLog("ERR", "Playback", $"Playback failed: {ex.Message}");
            _logger.LogError(ex, "Playback failed");
        }
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
