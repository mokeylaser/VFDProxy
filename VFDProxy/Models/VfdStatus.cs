namespace VFDProxy.Models;

public sealed class VfdStatus
{
    public bool   IsRunning          { get; init; }
    public bool   IsForward          { get; init; }
    public bool   IsReverse          { get; init; }
    public bool   IsFaulted          { get; init; }
    public double OutputFrequencyHz  { get; init; }
    public double TargetFrequencyHz  { get; init; }

    public static VfdStatus Unknown => new();
}
