namespace VFDProxy.Drivers;

public interface IGrblDriver : IDisposable
{
    /// <summary>Fires with each raw response line from GRBL (e.g. "ok", "error:9", "Grbl 1.1h...").</summary>
    event EventHandler<string>? ResponseReceived;

    Task OpenAsync(string portName, int baud, CancellationToken ct = default);

    /// <summary>
    /// Send a G-code line to GRBL, respecting the character-counting buffer protocol.
    /// Awaits until there is buffer room before writing.
    /// </summary>
    Task SendLineAsync(string line, CancellationToken ct = default);

    /// <summary>Send a real-time command byte (e.g. 0x18 = reset, '!' = feed hold).</summary>
    void SendRealtime(byte command);

    /// <summary>Query GRBL identity string ($I or pressing Enter).</summary>
    Task<string> ProbeAsync(CancellationToken ct = default);

    void Close();
    bool IsOpen { get; }
}
