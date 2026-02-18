using VFDProxy.Models;

namespace VFDProxy.ViewModels;

public sealed class JobBehaviorViewModel : ViewModelBase
{
    private bool   _stripSpindleCommands = true;
    private bool   _stripToolChanges     = true;
    private bool   _treatM0M1AsPause     = true;
    private bool   _stripCoolant         = false;
    private bool   _autoStopOnDisconnect = true;
    private bool   _m4IsCcw              = false;
    private int    _polePairs            = 1;
    private double _maxRpm               = 24000;
    private double _minHz                = 5.0;
    private double _maxHz                = 400.0;
    private byte   _vfdSlaveAddr         = 1;

    public bool   StripSpindleCommands { get => _stripSpindleCommands; set => SetField(ref _stripSpindleCommands, value); }
    public bool   StripToolChanges     { get => _stripToolChanges;     set => SetField(ref _stripToolChanges,     value); }
    public bool   TreatM0M1AsPause     { get => _treatM0M1AsPause;     set => SetField(ref _treatM0M1AsPause,     value); }
    public bool   StripCoolant         { get => _stripCoolant;         set => SetField(ref _stripCoolant,         value); }
    public bool   AutoStopOnDisconnect { get => _autoStopOnDisconnect; set => SetField(ref _autoStopOnDisconnect, value); }
    public bool   M4IsCcw              { get => _m4IsCcw;              set => SetField(ref _m4IsCcw,              value); }
    public int    PolePairs            { get => _polePairs;            set => SetField(ref _polePairs,            value); }
    public double MaxRpm               { get => _maxRpm;               set => SetField(ref _maxRpm,               value); }
    public double MinHz                { get => _minHz;                set => SetField(ref _minHz,                value); }
    public double MaxHz                { get => _maxHz;                set => SetField(ref _maxHz,                value); }
    public byte   VfdSlaveAddr         { get => _vfdSlaveAddr;         set => SetField(ref _vfdSlaveAddr,         value); }

    public void ApplyConfig(AppConfig config)
    {
        StripSpindleCommands = config.StripSpindleCommands;
        StripToolChanges     = config.StripToolChanges;
        TreatM0M1AsPause     = config.TreatM0M1AsPause;
        StripCoolant         = config.StripCoolantCommands;
        AutoStopOnDisconnect = config.AutoStopOnDisconnect;
        M4IsCcw              = config.M4IsCcw;
        PolePairs            = config.PolePairs;
        MaxRpm               = config.MaxRpm;
        MinHz                = config.MinHz;
        MaxHz                = config.MaxHz;
        VfdSlaveAddr         = config.VfdSlaveAddr;
    }

    public void PersistToConfig(AppConfig config)
    {
        config.StripSpindleCommands = StripSpindleCommands;
        config.StripToolChanges     = StripToolChanges;
        config.TreatM0M1AsPause     = TreatM0M1AsPause;
        config.StripCoolantCommands = StripCoolant;
        config.AutoStopOnDisconnect = AutoStopOnDisconnect;
        config.M4IsCcw              = M4IsCcw;
        config.PolePairs            = PolePairs;
        config.MaxRpm               = MaxRpm;
        config.MinHz                = MinHz;
        config.MaxHz                = MaxHz;
        config.VfdSlaveAddr         = VfdSlaveAddr;
    }
}
