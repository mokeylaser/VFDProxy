using VFDProxy.Models;

namespace VFDProxy.Drivers;

public interface IVfdDriver : IDisposable
{
    void Configure(byte slaveAddr);
    Task OpenAsync(string portName, int baud, CancellationToken ct = default);

    /// <summary>Set output frequency in Hz (e.g. 400.0 for 24000 RPM, 2-pole).</summary>
    Task SetFrequencyAsync(double hz, CancellationToken ct = default);

    Task RunCwAsync (CancellationToken ct = default);
    Task RunCcwAsync(CancellationToken ct = default);
    Task StopAsync  (CancellationToken ct = default);

    Task<VfdStatus> ReadStatusAsync(CancellationToken ct = default);

    void Close();
    bool IsOpen { get; }
}
