using System.IO.Ports;
using System.Text;

namespace VFDProxy.Drivers;

/// <summary>
/// Wraps the proxy-side end of the com0com virtual COM pair.
/// Candle connects to the other end; bytes flow through the kernel driver.
/// Baud rate is irrelevant for com0com but we set it to match Candle's expectation.
/// </summary>
public sealed class VirtualComDriver : IVirtualComDriver
{
    private SerialPort?    _port;
    private readonly object _writeLock = new();
    private readonly object _readLock  = new();
    private readonly StringBuilder _lineBuffer = new();

    public event EventHandler<string>? LineReceived;
    public bool IsOpen => _port?.IsOpen ?? false;

    public Task OpenAsync(string portName, int baud, CancellationToken ct = default)
    {
        _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
        {
            ReadTimeout  = SerialPort.InfiniteTimeout,
            WriteTimeout = 2000,
            Encoding     = Encoding.ASCII,
            NewLine      = "\n"
        };
        _port.DataReceived += OnDataReceived;
        _port.Open();
        return Task.CompletedTask;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_port is null || !_port.IsOpen) return;

        try
        {
            var data = _port.ReadExisting();
            lock (_readLock)
            {
                foreach (var ch in data)
                {
                    if (ch == '\n' || ch == '\r')
                    {
                        var line = _lineBuffer.ToString().Trim();
                        _lineBuffer.Clear();
                        if (line.Length > 0)
                            LineReceived?.Invoke(this, line);
                    }
                    else
                    {
                        _lineBuffer.Append(ch);
                    }
                }
            }
        }
        catch (InvalidOperationException) { /* port closed mid-read */ }
    }

    public void WriteResponse(string line)
    {
        lock (_writeLock)
        {
            try
            {
                _port?.Write(line + "\n");
            }
            catch (InvalidOperationException) { /* port closed */ }
        }
    }

    public void Close()
    {
        if (_port is null) return;
        _port.DataReceived -= OnDataReceived;
        if (_port.IsOpen)
        {
            try { _port.Close(); } catch { /* ignore */ }
        }
        _port.Dispose();
        _port = null;
    }

    public void Dispose() => Close();
}
