using System.Windows.Input;
using VFDProxy.Engine;
using VFDProxy.Infrastructure;

namespace VFDProxy.ViewModels;

public sealed class ControlsPanelViewModel : ViewModelBase
{
    private readonly IProxyEngine _engine;
    private double _manualRpm = 12000;
    private string _spindleStateDisplay = "Stopped";
    private bool   _isConnected;

    public double ManualRpm
    {
        get => _manualRpm;
        set => SetField(ref _manualRpm, Math.Max(0, value));
    }

    public string SpindleStateDisplay
    {
        get => _spindleStateDisplay;
        set => SetField(ref _spindleStateDisplay, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            SetField(ref _isConnected, value);
            ((AsyncRelayCommand)RunCwCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)RunCcwCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)StopSpindleCommand).RaiseCanExecuteChanged();
        }
    }

    public ICommand RunCwCommand        { get; }
    public ICommand RunCcwCommand       { get; }
    public ICommand StopSpindleCommand  { get; }
    public ICommand EmergencyStopCommand { get; }

    public ControlsPanelViewModel(IProxyEngine engine)
    {
        _engine = engine;

        RunCwCommand = new AsyncRelayCommand(
            () => _engine.SpindleSetRpmAsync(ManualRpm, cw: true),
            () => IsConnected);

        RunCcwCommand = new AsyncRelayCommand(
            () => _engine.SpindleSetRpmAsync(ManualRpm, cw: false),
            () => IsConnected);

        StopSpindleCommand = new AsyncRelayCommand(
            () => _engine.SpindleStopAsync(),
            () => IsConnected);

        EmergencyStopCommand = new AsyncRelayCommand(
            () => _engine.EmergencyStopAsync());
    }
}
