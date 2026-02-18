namespace VFDProxy.Models;

public sealed record ComPortInfo(string PortName, string FriendlyName)
{
    public string Display => $"{PortName}  —  {FriendlyName}";
    public override string ToString() => Display;
}
