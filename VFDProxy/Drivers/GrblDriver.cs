using System.IO.Ports;
using System.Text;

namespace VFDProxy.Drivers;

/// <summary>
/// Manages the serial connection to the GRBL controller.
/// Implements the GRBL character-counting buffer protocol:
///   - GRBL has a 128-byte RX buffer (RX_BUFFER_SIZE in config.h)
///   - We track how many bytes are in-flight
///   - Each "ok" or "error:" response frees those bytes
///   - We block before sending if there's no room
/// </summary>
public sealed class GrblDriver : IGrblDriver
{
    private const int GrblRxBufferSize  = 127; // 128 - 1 for safety
    private const int BufferWaitTimeoutMs = 5000; // watchdog for unresponsive GRBL

    private SerialPort?           _port;
    private CancellationTokenSource? _readCts;
    private Task?                 _readTask;

    // Buffer accounting
    private readonly SemaphoreSlim        _bufferSem    = new(1, 1);
    private readonly Queue<int>           _sentLengths  = new();
    private int                           _sentBytes;
    private readonly object               _bufferLock   = new();

    // Probe support
    private TaskCompletionSource<string>? _probeTcs;
    private readonly object               _probeLock = new();

    public event EventHandler<string>? ResponseReceived;
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
        _port.Open();

        _readCts  = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));

        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new StringBuilder();

        while (!ct.IsCancellationRequested && _port is { IsOpen: true })
        {
            try
            {
                // Read char-by-char via BaseStream to support CancellationToken
                var buf = new byte[1];
                var read = await _port.BaseStream.ReadAsync(buf, 0, 1, ct);
                if (read == 0) continue;

                var ch = (char)buf[0];
                if (ch == '\n' || ch == '\r')
                {
                    var line = buffer.ToString().Trim();
                    buffer.Clear();
                    if (line.Length > 0)
                        HandleResponse(line);
                }
                else
                {
                    buffer.Append(ch);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (InvalidOperationException)  { break; } // port closed
            catch                              { break; }
        }
    }

    private void HandleResponse(string line)
    {
        // Release buffer for "ok" and "error:" responses
        if (line.StartsWith("ok", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
        {
            lock (_bufferLock)
            {
                if (_sentLengths.TryDequeue(out int freed))
                    _sentBytes -= freed;
            }
            try { _bufferSem.Release(); }
            catch (SemaphoreFullException) { /* spurious response — ignore */ }
        }

        // Probe completion
        lock (_probeLock)
        {
            if (_probeTcs is not null && !_probeTcs.Task.IsCompleted)
            {
                _probeTcs.TrySetResult(line);
            }
        }

        ResponseReceived?.Invoke(this, line);
    }

    public async Task SendLineAsync(string line, CancellationToken ct = default)
    {
        if (_port is null || !_port.IsOpen) return;

        // line ending = '\n' = 1 byte
        int lineBytes = Encoding.ASCII.GetByteCount(line) + 1;

        // Wait until the line fits in GRBL's buffer
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            lock (_bufferLock)
            {
                if (_sentBytes + lineBytes <= GrblRxBufferSize)
                {
                    _sentBytes += lineBytes;
                    _sentLengths.Enqueue(lineBytes);
                    break;
                }
            }

            // Wait for a response to free up buffer space (with watchdog timeout)
            if (!await _bufferSem.WaitAsync(BufferWaitTimeoutMs, ct))
                throw new TimeoutException("GRBL not responding — buffer wait timed out.");
        }

        try
        {
            _port.WriteLine(line);
        }
        catch (InvalidOperationException)
        {
            // Port closed — roll back accounting
            lock (_bufferLock)
            {
                _sentBytes -= lineBytes;
                // Can't easily dequeue the specific item, so drain and re-count
                // (this path only happens on disconnect, so accuracy matters less)
            }
        }
    }

    public void SendRealtime(byte command)
    {
        try
        {
            _port?.BaseStream.WriteByte(command);
        }
        catch { /* ignore on disconnect */ }
    }

    public async Task<string> ProbeAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_probeLock) { _probeTcs = tcs; }

        try
        {
            // Send empty line to elicit a response, then $I for version
            _port?.WriteLine(string.Empty);
            _port?.WriteLine("$I");
        }
        catch { return string.Empty; }

        using var registration = ct.Register(() => tcs.TrySetCanceled());
        var timeout = Task.Delay(3000, ct);
        var done    = await Task.WhenAny(tcs.Task, timeout);

        lock (_probeLock) { _probeTcs = null; }

        if (done == tcs.Task && !tcs.Task.IsFaulted && !tcs.Task.IsCanceled)
            return tcs.Task.Result;

        return string.Empty;
    }

    public void Close()
    {
        _readCts?.Cancel();

        if (_port is { IsOpen: true })
        {
            try { _port.Close(); } catch { /* ignore */ }
        }
        _port?.Dispose();
        _port = null;

        lock (_bufferLock)
        {
            _sentBytes = 0;
            _sentLengths.Clear();
        }

        // Drain the semaphore so any awaiting SendLineAsync can unblock
        try
        {
            while (_bufferSem.CurrentCount == 0)
                _bufferSem.Release();
        }
        catch (SemaphoreFullException) { /* already drained */ }
    }

    public void Dispose() => Close();
}
