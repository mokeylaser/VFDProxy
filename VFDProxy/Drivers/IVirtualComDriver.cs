namespace VFDProxy.Drivers;

public interface IVirtualComDriver : IDisposable
{
    /// <summary>Fires with each complete line received from Candle (no trailing newline).</summary>
    event EventHandler<string>? LineReceived;

    Task OpenAsync(string portName, int baud, CancellationToken ct = default);
    void WriteResponse(string line);  // writes line + "\n" back to Candle
    void Close();
    bool IsOpen { get; }
}
