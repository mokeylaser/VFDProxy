using System.Windows.Input;
using VFDProxy.Drivers;
using VFDProxy.Engine;
using VFDProxy.Infrastructure;
using VFDProxy.Models;
using VFDProxy.Services;

namespace VFDProxy.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IProxyEngine          _engine;
    private          AppConfig             _config;
    private          ProxyState            _state = ProxyState.Disconnected;
    private          string?               _errorMessage;
    private          string                _statusText = "Disconnected";
    private          VfdStatus             _vfdStatus  = VfdStatus.Unknown;

    public ConnectionPanelViewModel ConnectionPanel { get; }
    public ControlsPanelViewModel   ControlsPanel   { get; }
    public JobBehaviorViewModel     JobBehavior     { get; }
    public LogPanelViewModel        Log             { get; }

    public ProxyState State
    {
        get => _state;
        private set
        {
            SetField(ref _state, value);
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsDisconnected));
            OnPropertyChanged(nameof(CanConnect));
            ((AsyncRelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
            ControlsPanel.IsConnected = value == ProxyState.Running;
        }
    }

    public bool IsConnected    => State == ProxyState.Running;
    public bool IsDisconnected => State == ProxyState.Disconnected || State == ProxyState.Error;
    public bool CanConnect     => State == ProxyState.Disconnected || State == ProxyState.Error;

    public string? ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }
    public string  StatusText   { get => _statusText;   private set => SetField(ref _statusText,   value); }

    public VfdStatus VfdStatus
    {
        get => _vfdStatus;
        private set => SetField(ref _vfdStatus, value);
    }

    public ICommand ConnectCommand    { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand EmergencyStopCommand { get; }

    public MainWindowViewModel()
    {
        _config = ConfigService.Load();

        var virtualDriver = new VirtualComDriver();
        var grblDriver    = new GrblDriver();
        var vfdDriver     = new VfdDriver();

        _engine = new ProxyEngine(virtualDriver, grblDriver, vfdDriver);
        _engine.StateChanged    += OnStateChanged;
        _engine.LogMessage      += OnLogMessage;
        _engine.VfdStatusUpdated += OnVfdStatusUpdated;

        ConnectionPanel = new ConnectionPanelViewModel();
        ControlsPanel   = new ControlsPanelViewModel(_engine);
        JobBehavior     = new JobBehaviorViewModel();
        Log             = new LogPanelViewModel();

        // Wire port scan logging so diagnostics appear in the log panel
        ConnectionPanel.LogCallback = entry => Log.Add(entry);

        // Report config load errors to the log
        if (ConfigService.LastError is not null)
            Log.Add(LogEntry.Warn($"{ConfigService.LastError} — using defaults."));

        ConnectionPanel.ApplyConfig(_config);
        JobBehavior.ApplyConfig(_config);

        var connectCmd = new AsyncRelayCommand(ConnectAsync, () => CanConnect);
        connectCmd.CommandFailed += (_, ex) => Log.Add(LogEntry.Error($"Connect failed: {ex.Message}"));

        var disconnectCmd = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
        disconnectCmd.CommandFailed += (_, ex) => Log.Add(LogEntry.Error($"Disconnect failed: {ex.Message}"));

        var estopCmd = new AsyncRelayCommand(() => _engine.EmergencyStopAsync());
        estopCmd.CommandFailed += (_, ex) => Log.Add(LogEntry.Error($"Emergency stop failed: {ex.Message}"));

        ConnectCommand = connectCmd;
        DisconnectCommand = disconnectCmd;
        EmergencyStopCommand = estopCmd;

        // Trigger initial port enumeration
        _ = ConnectionPanel.RefreshPortsCommand.TryExecute();
    }

    private async Task ConnectAsync()
    {
        ConnectionPanel.PersistToConfig(_config);
        JobBehavior.PersistToConfig(_config);

        // Validate required port selections before attempting to connect
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_config.VirtualPortProxy))
            missing.Add("Virtual COM (Proxy side)");
        if (string.IsNullOrWhiteSpace(_config.VirtualPortCandle))
            missing.Add("Virtual COM (Candle side)");
        if (string.IsNullOrWhiteSpace(_config.GrblPort))
            missing.Add("GRBL Port");
        if (string.IsNullOrWhiteSpace(_config.VfdPort))
            missing.Add("VFD Port");

        if (missing.Count > 0)
        {
            Log.Add(LogEntry.Error($"Cannot connect — the following ports are not configured: {string.Join(", ", missing)}"));
            Log.Add(LogEntry.Info("Please select a COM port for each connection before clicking Connect."));
            return;
        }

        ConfigService.Save(_config);

        await _engine.StartAsync(_config);
    }

    private async Task DisconnectAsync()
    {
        await _engine.StopAsync();
    }

    private void OnStateChanged(object? sender, StateChangedEventArgs e)
    {
        DispatcherService.Invoke(() =>
        {
            State = e.Current;
            ErrorMessage = e.ErrorMessage;
            StatusText = e.Current switch
            {
                ProxyState.Disconnected => "Disconnected",
                ProxyState.Connecting   => "Connecting...",
                ProxyState.Running      => "Running",
                ProxyState.Error        => $"Error: {e.ErrorMessage}",
                _                       => "Unknown"
            };
            ConnectionPanel.GrblStatus    = IsConnected ? "Connected" : "Disconnected";
            ConnectionPanel.VfdStatus     = IsConnected ? "Connected" : "Disconnected";
            ConnectionPanel.VirtualStatus = IsConnected ? "Active"    : "Inactive";
        });
    }

    private void OnLogMessage(object? sender, LogMessageEventArgs e) =>
        Log.Add(e.Entry);

    private void OnVfdStatusUpdated(object? sender, VfdStatusUpdatedEventArgs e)
    {
        DispatcherService.Invoke(() =>
        {
            VfdStatus = e.Status;
            int polePairs = Math.Max(1, _config.PolePairs);
            double rpm = e.Status.OutputFrequencyHz * 60.0 / polePairs;
            ControlsPanel.SpindleStateDisplay = e.Status.IsRunning
                ? $"{(e.Status.IsForward ? "CW" : "CCW")} {rpm:F0} RPM"
                : "Stopped";
        });
    }

    public void OnWindowClosing()
    {
        ConnectionPanel.PersistToConfig(_config);
        JobBehavior.PersistToConfig(_config);
        ConfigService.Save(_config);

        // Block until the VFD stop command is actually sent — do not let the
        // process exit while the spindle may still be running.
        try { _engine.EmergencyStopAsync().GetAwaiter().GetResult(); }
        catch { /* best-effort — process is exiting */ }
    }
}

// Extension to allow ICommand.TryExecute() on commands that may not have parameters
file static class CommandExtensions
{
    public static bool TryExecute(this ICommand command, object? parameter = null)
    {
        if (!command.CanExecute(parameter)) return false;
        command.Execute(parameter);
        return true;
    }
}
