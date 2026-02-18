using System.Management;
using VFDProxy.Models;

namespace VFDProxy.Services;

/// <summary>
/// Enumerates COM ports with friendly names from WMI.
/// Falls back to System.IO.Ports.SerialPort.GetPortNames() if WMI fails.
/// </summary>
public static class ComPortEnumerator
{
    public static async Task<IReadOnlyList<ComPortInfo>> GetPortsAsync()
    {
        return await Task.Run(GetPorts);
    }

    public static IReadOnlyList<ComPortInfo> GetPorts()
    {
        var result = new List<ComPortInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? string.Empty;
                // Extract "COMx" from the friendly name like "USB Serial Device (COM3)"
                var portName = ExtractPortName(name);
                if (portName is not null)
                    result.Add(new ComPortInfo(portName, name));
            }
        }
        catch
        {
            // WMI unavailable — fall back to bare port names
        }

        // Add any ports not picked up by WMI (rare but possible)
        var wmiBound = new HashSet<string>(result.Select(p => p.PortName), StringComparer.OrdinalIgnoreCase);
        foreach (var port in System.IO.Ports.SerialPort.GetPortNames())
        {
            if (!wmiBound.Contains(port))
                result.Add(new ComPortInfo(port, port));
        }

        var comparer = new NaturalPortComparer();
        return result
            .OrderBy(p => p.PortName, comparer)
            .ToList();
    }

    private static string? ExtractPortName(string friendlyName)
    {
        var start = friendlyName.LastIndexOf('(');
        var end   = friendlyName.LastIndexOf(')');
        if (start < 0 || end < 0 || end <= start) return null;
        var candidate = friendlyName[(start + 1)..end];
        return candidate.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ? candidate.ToUpperInvariant() : null;
    }

    /// <summary>Sorts COM1 COM2 COM10 COM20 numerically instead of lexicographically.</summary>
    private sealed class NaturalPortComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            // Strip "COM" prefix and compare numerically
            if (x.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                y.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(x[3..], out int xi) &&
                int.TryParse(y[3..], out int yi))
                return xi.CompareTo(yi);

            return StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }
    }
}
