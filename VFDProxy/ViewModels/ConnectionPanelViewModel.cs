using System.Collections.ObjectModel;
using System.Windows.Input;
using VFDProxy.Infrastructure;
using VFDProxy.Models;
using VFDProxy.Services;

namespace VFDProxy.ViewModels;

public sealed class ConnectionPanelViewModel : ViewModelBase
{
    private string? _virtualPortCandle;
    private string? _virtualPortProxy;
    private string? _grblPort;
    private int     _grblBaud = 115200;
    private string? _vfdPort;
    private int     _vfdBaud = 9600;
    private string  _grblStatus    = "Disconnected";
    private string  _vfdStatus     = "Disconnected";
    private string  _virtualStatus = "Disconnected";

    public ObservableCollection<ComPortInfo> AllPorts     { get; } = new();
    public ObservableCollection<int>         BaudRates    { get; } = new(new[] { 9600, 19200, 38400, 57600, 115200, 250000 });

    public string? VirtualPortCandle { get => _virtualPortCandle; set => SetField(ref _virtualPortCandle, value); }
    public string? VirtualPortProxy  { get => _virtualPortProxy;  set => SetField(ref _virtualPortProxy,  value); }
    public string? GrblPort          { get => _grblPort;          set => SetField(ref _grblPort,          value); }
    public int     GrblBaud          { get => _grblBaud;          set => SetField(ref _grblBaud,          value); }
    public string? VfdPort           { get => _vfdPort;           set => SetField(ref _vfdPort,           value); }
    public int     VfdBaud           { get => _vfdBaud;           set => SetField(ref _vfdBaud,           value); }

    public string GrblStatus    { get => _grblStatus;    set => SetField(ref _grblStatus,    value); }
    public string VfdStatus     { get => _vfdStatus;     set => SetField(ref _vfdStatus,     value); }
    public string VirtualStatus { get => _virtualStatus; set => SetField(ref _virtualStatus, value); }

    /// <summary>
    /// Optional callback for logging port scan results. Set by MainWindowViewModel.
    /// </summary>
    public Action<LogEntry>? LogCallback { get; set; }

    public ICommand RefreshPortsCommand { get; }

    public ConnectionPanelViewModel()
    {
        RefreshPortsCommand = new AsyncRelayCommand(RefreshPortsAsync);
    }

    private async Task RefreshPortsAsync()
    {
        IReadOnlyList<ComPortInfo> ports;
        try
        {
            ports = await ComPortEnumerator.GetPortsAsync();
        }
        catch (Exception ex)
        {
            LogCallback?.Invoke(LogEntry.Error($"Port scan failed: {ex.Message}"));
            return;
        }

        // Save selections
        var prevCandle = VirtualPortCandle;
        var prevProxy  = VirtualPortProxy;
        var prevGrbl   = GrblPort;
        var prevVfd    = VfdPort;

        Infrastructure.DispatcherService.Invoke(() =>
        {
            AllPorts.Clear();
            foreach (var p in ports) AllPorts.Add(p);

            // Restore selections if still present
            VirtualPortCandle = ports.Any(p => p.PortName == prevCandle) ? prevCandle : null;
            VirtualPortProxy  = ports.Any(p => p.PortName == prevProxy)  ? prevProxy  : null;
            GrblPort          = ports.Any(p => p.PortName == prevGrbl)   ? prevGrbl   : null;
            VfdPort           = ports.Any(p => p.PortName == prevVfd)    ? prevVfd    : null;
        });

        // Log scan results
        if (ComPortEnumerator.LastDiagnostic is not null)
        {
            LogCallback?.Invoke(LogEntry.Warn(ComPortEnumerator.LastDiagnostic));
        }

        if (ports.Count == 0)
        {
            LogCallback?.Invoke(LogEntry.Warn(
                "No COM ports found. Troubleshooting: " +
                "(1) Check Device Manager for COM ports under 'Ports (COM & LPT)'. " +
                "(2) Ensure USB-serial drivers are installed for your devices. " +
                "(3) Verify com0com virtual port pair is configured. " +
                "(4) Try running VFDProxy as Administrator if ports exist but aren't listed."));
        }
        else
        {
            LogCallback?.Invoke(LogEntry.Info(
                $"Port scan: found {ports.Count} port(s): {string.Join(", ", ports.Select(p => p.PortName))}"));

            // Check for com0com virtual port pairs
            bool hasVirtualPorts = ports.Any(p =>
                p.FriendlyName.Contains("com0com", StringComparison.OrdinalIgnoreCase) ||
                p.FriendlyName.Contains("virtual", StringComparison.OrdinalIgnoreCase) ||
                p.FriendlyName.Contains("emulator", StringComparison.OrdinalIgnoreCase));

            if (!hasVirtualPorts)
            {
                LogCallback?.Invoke(LogEntry.Warn(
                    "No virtual COM port pair detected. VFDProxy requires a virtual serial port pair " +
                    "(e.g., com0com) so that Candle can communicate through VFDProxy. " +
                    "Install com0com and create a port pair, or type the port names manually in the dropdowns."));
            }
        }
    }

    public void ApplyConfig(AppConfig config)
    {
        VirtualPortCandle = config.VirtualPortCandle;
        VirtualPortProxy  = config.VirtualPortProxy;
        GrblPort          = config.GrblPort;
        GrblBaud          = config.GrblBaud;
        VfdPort           = config.VfdPort;
        VfdBaud           = config.VfdBaud;
    }

    public void PersistToConfig(AppConfig config)
    {
        config.VirtualPortCandle = VirtualPortCandle ?? string.Empty;
        config.VirtualPortProxy  = VirtualPortProxy  ?? string.Empty;
        config.GrblPort          = GrblPort          ?? string.Empty;
        config.GrblBaud          = GrblBaud;
        config.VfdPort           = VfdPort           ?? string.Empty;
        config.VfdBaud           = VfdBaud;
    }
}
