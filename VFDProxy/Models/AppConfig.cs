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
    public double HzPerRpm => 1.0 / (60.0 / Math.Max(1, PolePairs)); // Hz = RPM * polePairs / 60

    /// <summary>
    /// Validates and clamps all values to safe ranges. Returns a list of issues found (empty if valid).
    /// </summary>
    public List<string> Validate()
    {
        var issues = new List<string>();

        if (PolePairs < 1)
        {
            issues.Add($"PolePairs was {PolePairs}, clamped to 1.");
            PolePairs = 1;
        }

        if (MaxRpm <= 0)
        {
            issues.Add($"MaxRpm was {MaxRpm}, reset to 24000.");
            MaxRpm = 24000;
        }

        if (MinHz < 0)
        {
            issues.Add($"MinHz was {MinHz}, clamped to 0.");
            MinHz = 0;
        }

        if (MaxHz <= 0)
        {
            issues.Add($"MaxHz was {MaxHz}, reset to 400.");
            MaxHz = 400;
        }

        if (MinHz >= MaxHz)
        {
            issues.Add($"MinHz ({MinHz}) >= MaxHz ({MaxHz}), reset to defaults 5/400.");
            MinHz = 5.0;
            MaxHz = 400.0;
        }

        if (VfdSlaveAddr < 1 || VfdSlaveAddr > 247)
        {
            issues.Add($"VfdSlaveAddr was {VfdSlaveAddr}, clamped to valid Modbus range [1-247].");
            VfdSlaveAddr = Math.Clamp(VfdSlaveAddr, (byte)1, (byte)247);
        }

        if (GrblBaud <= 0)
        {
            issues.Add($"GrblBaud was {GrblBaud}, reset to 115200.");
            GrblBaud = 115200;
        }

        if (VfdBaud <= 0)
        {
            issues.Add($"VfdBaud was {VfdBaud}, reset to 9600.");
            VfdBaud = 9600;
        }

        if (MaxLogLines < 100)
        {
            issues.Add($"MaxLogLines was {MaxLogLines}, clamped to 100.");
            MaxLogLines = 100;
        }

        return issues;
    }
}
