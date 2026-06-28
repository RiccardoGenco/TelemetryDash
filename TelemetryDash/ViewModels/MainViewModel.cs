using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Reactive.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

    private static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CriticalSoundCooldown = TimeSpan.FromSeconds(5);
    private readonly DispatcherTimer _staleTimer;
    private readonly DispatcherTimer _notificationTimer;
    private DateTime _lastCriticalSoundUtc = DateTime.MinValue;

    private string? _lastReportPath;
    private TimeSpan _playbackDuration;
    private bool _suppressSeek;

    // Frozen theme brushes (safe to share across threads / bindings)
    private static readonly Brush GreenBrush = Frozen(0x38, 0xF0, 0x6A);
    private static readonly Brush AmberBrush = Frozen(0xFF, 0xB0, 0x00);
    private static readonly Brush RedBrush = Frozen(0xFF, 0x55, 0x55);

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
            {
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(ConnectionStatusBrush));
            }
        }
    }

    public bool IsConnected => ConnectionStatus == ConnectionStatus.Connected;

    public Brush ConnectionStatusBrush => ConnectionStatus switch
    {
        ConnectionStatus.Connected => GreenBrush,
        ConnectionStatus.Connecting => AmberBrush,
        _ => RedBrush,
    };

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

    private double _playbackPosition;
    public double PlaybackPosition
    {
        get => _playbackPosition;
        set
        {
            if (SetProperty(ref _playbackPosition, value) && !_suppressSeek)
                _playbackService.SeekToFraction(value);
        }
    }

    private string _playbackTimeLabel = "00:00 / 00:00";
    public string PlaybackTimeLabel
    {
        get => _playbackTimeLabel;
        set => SetProperty(ref _playbackTimeLabel, value);
    }

    private int _activeAlarmCount;
    public int ActiveAlarmCount
    {
        get => _activeAlarmCount;
        set
        {
            if (SetProperty(ref _activeAlarmCount, value))
                OnPropertyChanged(nameof(HasAlarms));
        }
    }

    public bool HasAlarms => _activeAlarmCount > 0;

    public bool HasChannels => Channels.Count > 0;

    // ----- Notification banner / toast -----
    private string? _notificationText;
    public string? NotificationText
    {
        get => _notificationText;
        set => SetProperty(ref _notificationText, value);
    }

    private Brush _notificationBrush = Frozen(0xFF, 0xB0, 0x00);
    public Brush NotificationBrush
    {
        get => _notificationBrush;
        set => SetProperty(ref _notificationBrush, value);
    }

    private bool _notificationVisible;
    public bool NotificationVisible
    {
        get => _notificationVisible;
        set => SetProperty(ref _notificationVisible, value);
    }

    private bool _notificationShowOpen;
    public bool NotificationShowOpen
    {
        get => _notificationShowOpen;
        set => SetProperty(ref _notificationShowOpen, value);
    }

    public bool CanOpenReport => _lastReportPath is not null && File.Exists(_lastReportPath);

    // ----- Settings -----
    private bool _isSettingsOpen;
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public ObservableCollection<AlarmConfigViewModel> AlarmConfigs { get; } = new();

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
    public ICommand AcknowledgeAlarmsCommand { get; }
    public ICommand OpenReportCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand SaveSettingsCommand { get; }

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
        AcknowledgeAlarmsCommand = new RelayCommand(AcknowledgeAlarms, () => HasAlarms);
        OpenReportCommand = new RelayCommand(OpenReport, () => CanOpenReport);
        ToggleSettingsCommand = new RelayCommand(ToggleSettings);
        SaveSettingsCommand = new RelayCommand(SaveSettings);

        _playbackService.OnReading += OnPlaybackReading;
        _playbackService.OnStateChanged += OnPlaybackStateChanged;
        _playbackService.OnProgress += OnPlaybackProgress;

        Channels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChannels));

        _staleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _staleTimer.Tick += (_, _) => CheckStaleChannels();
        _staleTimer.Start();

        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _notificationTimer.Tick += (_, _) =>
        {
            _notificationTimer.Stop();
            NotificationVisible = false;
        };

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
        var configs = LoadAlarmConfigs() ?? DefaultAlarmConfigs();
        foreach (var config in configs)
            _alarmService.AddConfig(config);
    }

    private static List<AlarmConfig> DefaultAlarmConfigs() => new()
    {
        new AlarmConfig { ChannelId = "TEMP_A1", MinValue = 30.0, MaxValue = 85.0, Severity = AlarmSeverity.Warning },
        new AlarmConfig { ChannelId = "PRESS_B2", MinValue = 900.0, MaxValue = 1100.0, Severity = AlarmSeverity.Critical },
        new AlarmConfig { ChannelId = "VIB_C3", MinValue = 0.0, MaxValue = 1.2, Severity = AlarmSeverity.Warning },
        new AlarmConfig { ChannelId = "FLOW_D4", MinValue = 80.0, MaxValue = 160.0, Severity = AlarmSeverity.Warning },
    };

    private async Task ConnectAsync()
    {
        _activePlugin = _pluginLoader.GetPlugin(SelectedPlugin);
        if (_activePlugin is null)
        {
            AddLog("ERR", "Connection", $"Plugin '{SelectedPlugin}' not found");
            Notify($"Plugin '{SelectedPlugin}' not found", RedBrush);
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
            Notify(ex.Message, RedBrush);
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
        var alarmResult = _alarmService.Evaluate(reading);
        UpdateUI(reading, alarmResult);

        if (alarmResult.IsAlarm)
            AddAlarmToUI(alarmResult);

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
                _ = PersistBatchAsync(batch);

            if (alarmResult.IsAlarm)
            {
                _ = _sessionRepository.SaveAlarmAsync(_currentSessionId.Value, alarmResult);
                _fileLogger.Log("WRN", "Alarm", alarmResult.Message);
            }
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
            var alarmResult = _alarmService.Evaluate(reading);
            UpdateUI(reading, alarmResult);

            if (alarmResult.IsAlarm)
                AddAlarmToUI(alarmResult);
        });
    }

    private void OnPlaybackStateChanged(PlaybackState state)
    {
        Application.Current?.Dispatcher.Invoke(() => PlaybackState = state);
    }

    private void OnPlaybackProgress(int index)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var total = _playbackService.TotalReadings;
            _suppressSeek = true;
            PlaybackPosition = total > 1 ? (double)index / (total - 1) : 0;
            _suppressSeek = false;

            var elapsed = total > 1
                ? TimeSpan.FromTicks((long)(_playbackDuration.Ticks * ((double)index / (total - 1))))
                : TimeSpan.Zero;
            PlaybackTimeLabel = $"{elapsed:mm\\:ss} / {_playbackDuration:mm\\:ss}";
        });
    }

    /// <summary>
    /// Shared UI update logic for both live and playback readings.
    /// </summary>
    private void UpdateUI(TelemetryReading reading, AlarmResult alarmResult)
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
        channel.AlarmState = alarmResult.IsAlarm ? alarmResult.Severity : null;
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

        ActiveAlarmCount = Alarms.Count;

        if (alarmResult.Severity == AlarmSeverity.Critical &&
            DateTime.UtcNow - _lastCriticalSoundUtc > CriticalSoundCooldown)
        {
            _lastCriticalSoundUtc = DateTime.UtcNow;
            try { SystemSounds.Hand.Play(); } catch { /* audio not available */ }
        }
    }

    private void AcknowledgeAlarms()
    {
        Alarms.Clear();
        _alarmService.ClearAlarms();
        ActiveAlarmCount = 0;
    }

    private void CheckStaleChannels()
    {
        if (!IsLiveMode || ConnectionStatus != ConnectionStatus.Connected)
            return;

        var now = DateTime.UtcNow;
        foreach (var ch in Channels)
        {
            if (now - ch.LastUpdateUtc > StaleThreshold)
                ch.IsStale = true;
        }
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
        Notify(ex.Message, RedBrush);
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
            Notify("No session available for report", AmberBrush);
            return;
        }

        try
        {
            AddLog("INFO", "Report", "Generating report...");
            var session = await _sessionRepository.GetSessionAsync(sessionId);
            if (session is null || session.Readings.Count == 0)
            {
                AddLog("WRN", "Report", "Session has no data to report");
                Notify("Session has no data to report", AmberBrush);
                return;
            }

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TelemetryDash");
            Directory.CreateDirectory(dir);
            var outputPath = Path.Combine(dir, $"Report_{sessionId:N}.pdf");

            await _reportGenerator.GenerateAsync(session, outputPath);
            AddLog("INFO", "Report", $"Report saved to {outputPath}");
            _fileLogger.Log("INFO", "Report", $"Generated: {outputPath}");

            _lastReportPath = outputPath;
            OnPropertyChanged(nameof(CanOpenReport));
            Notify("Report saved", GreenBrush, showOpen: true);
        }
        catch (Exception ex)
        {
            AddLog("ERR", "Report", $"Report generation failed: {ex.Message}");
            _logger.LogError(ex, "Report generation failed");
            Notify($"Report failed: {ex.Message}", RedBrush);
        }
    }

    private void OpenReport()
    {
        if (string.IsNullOrEmpty(_lastReportPath) || !File.Exists(_lastReportPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(_lastReportPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open report");
        }
    }

    private async Task TogglePlaybackModeAsync()
    {
        if (!IsPlaybackMode)
        {
            Channels.Clear();
            Alarms.Clear();
            ActiveAlarmCount = 0;

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
            ActiveAlarmCount = 0;
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
            ActiveAlarmCount = 0;
            _alarmService.ClearAlarms();

            var session = await _playbackService.LoadSessionAsync(SelectedSession.Id);
            _playbackDuration = session.Readings.Count > 1
                ? session.Readings[^1].Timestamp - session.Readings[0].Timestamp
                : TimeSpan.Zero;
            PlaybackPosition = 0;
            PlaybackTimeLabel = $"00:00 / {_playbackDuration:mm\\:ss}";

            _playbackService.Play();
            AddLog("INFO", "Playback", $"Playing session {SelectedSession.Id}");
        }
        catch (Exception ex)
        {
            AddLog("ERR", "Playback", $"Playback failed: {ex.Message}");
            _logger.LogError(ex, "Playback failed");
            Notify($"Playback failed: {ex.Message}", RedBrush);
        }
    }

    // ----- Settings -----
    private void ToggleSettings()
    {
        if (!IsSettingsOpen)
        {
            AlarmConfigs.Clear();
            foreach (var config in _alarmService.GetConfigs().OrderBy(c => c.ChannelId))
                AlarmConfigs.Add(new AlarmConfigViewModel(config));
            IsSettingsOpen = true;
        }
        else
        {
            IsSettingsOpen = false;
        }
    }

    private void SaveSettings()
    {
        var configs = AlarmConfigs.Select(c => c.ToConfig()).ToList();
        foreach (var config in configs)
            _alarmService.AddConfig(config);

        SaveAlarmConfigs(configs);
        IsSettingsOpen = false;
        Notify("Settings saved", GreenBrush);
        AddLog("INFO", "Settings", "Alarm thresholds updated");
    }

    private void Notify(string text, Brush brush, bool showOpen = false)
    {
        NotificationText = text;
        NotificationBrush = brush;
        NotificationShowOpen = showOpen;
        NotificationVisible = true;
        _notificationTimer.Stop();
        _notificationTimer.Start();
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

    // ----- Settings persistence (JSON) -----
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TelemetryDash", "settings.json");

    private List<AlarmConfig>? LoadAlarmConfigs()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var json = File.ReadAllText(SettingsPath);
            var configs = JsonSerializer.Deserialize<List<AlarmConfig>>(json);
            return configs is { Count: > 0 } ? configs : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load alarm settings");
            return null;
        }
    }

    private void SaveAlarmConfigs(List<AlarmConfig> configs)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save alarm settings");
        }
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static string GetUnit(string channelId) => channelId switch
    {
        "TEMP_A1" => "°C",
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
