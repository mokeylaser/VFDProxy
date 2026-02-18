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

    public ICommand RefreshPortsCommand { get; }

    public ConnectionPanelViewModel()
    {
        RefreshPortsCommand = new AsyncRelayCommand(RefreshPortsAsync);
    }

    private async Task RefreshPortsAsync()
    {
        var ports = await ComPortEnumerator.GetPortsAsync();

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
