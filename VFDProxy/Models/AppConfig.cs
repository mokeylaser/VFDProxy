using System.Text.Json.Serialization;

namespace VFDProxy.Models;

public class AppConfig
{
    // Virtual COM pair
    public string VirtualPortCandle { get; set; } = string.Empty;  // Candle-side (e.g. COM20)
    public string VirtualPortProxy  { get; set; } = string.Empty;  // Proxy-side (e.g. COM21)

    // GRBL
    public string GrblPort  { get; set; } = string.Empty;
    public int    GrblBaud  { get; set; } = 115200;

    // VFD RS-485
    public string VfdPort        { get; set; } = string.Empty;
    public int    VfdBaud        { get; set; } = 9600;
    public byte   VfdSlaveAddr   { get; set; } = 1;
    public int    PolePairs      { get; set; } = 1;   // 1 pole-pair → 2-pole motor (typical)
    public double MaxRpm         { get; set; } = 24000;
    public double MinHz          { get; set; } = 5.0;
    public double MaxHz          { get; set; } = 400.0;
    public bool   M4IsCcw        { get; set; } = false; // default: both M3/M4 run CW

    // Job behaviour
    public bool StripSpindleCommands  { get; set; } = true;
    public bool StripToolChanges      { get; set; } = true;
    public bool TreatM0M1AsPause      { get; set; } = true;
    public bool StripCoolantCommands  { get; set; } = false;
    public bool AutoStopOnDisconnect  { get; set; } = true;

    // UI
    public int MaxLogLines { get; set; } = 2000;

    [JsonIgnore]
    public double HzPerRpm => 1.0 / (60.0 / PolePairs); // Hz = RPM * polePairs / 60
}
