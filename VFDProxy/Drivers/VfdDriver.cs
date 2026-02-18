using System.IO.Ports;
using VFDProxy.Models;

namespace VFDProxy.Drivers;

/// <summary>
/// Huanyang VFD driver over RS-485.
///
/// Protocol summary (non-standard Modbus-like):
///   Frame: [SlaveAddr][FuncCode][DataLen][Data...][CRC_Lo][CRC_Hi]
///   CRC: CRC-16 Modbus (polynomial 0xA001, init 0xFFFF), applied to all bytes before the CRC.
///   Frequency unit: 0.01 Hz (so 400 Hz → 40000 → 0x9C40)
///
/// RS-485 note: USB adapters typically use RTS for direction control.
///   RtsEnable = true  → transmit mode
///   RtsEnable = false → receive mode
///   We toggle this around each write.
/// </summary>
public sealed class VfdDriver : IVfdDriver
{
    private SerialPort?         _port;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private byte                _slaveAddr = 1;
    private const int           ResponseTimeoutMs = 500;

    public bool IsOpen => _port?.IsOpen ?? false;

    public void Configure(byte slaveAddr) => _slaveAddr = slaveAddr;

    public Task OpenAsync(string portName, int baud, CancellationToken ct = default)
    {
        _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
        {
            ReadTimeout  = ResponseTimeoutMs,
            WriteTimeout = 1000,
            RtsEnable    = false,   // receive mode initially
            DtrEnable    = true
        };
        _port.Open();
        return Task.CompletedTask;
    }

    // ────────────────────────────────────────────────────────────
    // Public commands — all serialize through _lock
    // ────────────────────────────────────────────────────────────

    /// <summary>Set frequency, then run CW. Call sequence for M3 Sxxxx.</summary>
    public async Task SetFrequencyAsync(double hz, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try   { await ExecSetFrequency(hz, ct); }
        finally { _lock.Release(); }
    }

    public async Task RunCwAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try   { await ExecRunDirection(0x01, ct); }
        finally { _lock.Release(); }
    }

    public async Task RunCcwAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try   { await ExecRunDirection(0x02, ct); }
        finally { _lock.Release(); }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        // Stop uses a short timeout override — safety critical
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ResponseTimeoutMs * 2);

        await _lock.WaitAsync(cts.Token).ConfigureAwait(false);
        try   { await ExecStop(cts.Token); }
        finally { _lock.Release(); }
    }

    public async Task<VfdStatus> ReadStatusAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try   { return await ExecReadStatus(ct); }
        finally { _lock.Release(); }
    }

    // ────────────────────────────────────────────────────────────
    // Frame execution helpers
    // ────────────────────────────────────────────────────────────

    private async Task ExecSetFrequency(double hz, CancellationToken ct)
    {
        // Clamp to [0, 400] Hz
        hz = Math.Clamp(hz, 0, 400);
        ushort freq = (ushort)Math.Round(hz * 100);

        // FuncCode 0x05, DataLen 0x02, FreqHi, FreqLo
        var frame = BuildFrame(0x05, new byte[] { (byte)(freq >> 8), (byte)(freq & 0xFF) });
        await TransactAsync(frame, 6, ct);
    }

    private async Task ExecRunDirection(byte dirByte, CancellationToken ct)
    {
        // FuncCode 0x01, DataLen 0x01, DirectionByte
        var frame = BuildFrame(0x01, new byte[] { dirByte });
        await TransactAsync(frame, 6, ct);
    }

    private async Task ExecStop(CancellationToken ct)
    {
        // FuncCode 0x01, DataLen 0x01, StopByte = 0x08
        var frame = BuildFrame(0x01, new byte[] { 0x08 });
        await TransactAsync(frame, 6, ct);
    }

    private async Task<VfdStatus> ExecReadStatus(CancellationToken ct)
    {
        // FuncCode 0x03, DataLen 0x02, RegAddrHi=0x00, RegAddrLo=0x01
        var frame = BuildFrame(0x03, new byte[] { 0x00, 0x01 });
        var response = await TransactAsync(frame, 8, ct);

        if (response is null || response.Length < 8)
            return VfdStatus.Unknown;

        // Response: [addr][0x03][0x03][StatusByte][FreqHi][FreqLo][CRC_Lo][CRC_Hi]
        var statusByte = response[3];
        ushort freqRaw = (ushort)((response[4] << 8) | response[5]);
        double freqHz  = freqRaw / 100.0;

        return new VfdStatus
        {
            IsRunning         = (statusByte & 0x01) != 0,
            IsForward         = (statusByte & 0x02) != 0,
            IsReverse         = (statusByte & 0x04) != 0,
            IsFaulted         = (statusByte & 0x10) != 0,
            OutputFrequencyHz = freqHz
        };
    }

    // ────────────────────────────────────────────────────────────
    // Low-level transport
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Send a frame and read exactly <paramref name="expectedBytes"/> back.
    /// Returns null on timeout or port error.
    /// </summary>
    private async Task<byte[]?> TransactAsync(byte[] frame, int expectedBytes, CancellationToken ct)
    {
        if (_port is null || !_port.IsOpen) return null;

        try
        {
            // Switch to TX
            _port.RtsEnable = true;
            _port.DiscardInBuffer();

            _port.Write(frame, 0, frame.Length);
            _port.BaseStream.Flush();

            // Brief TX drain — at 9600 baud, 6 bytes = ~6.25 ms
            await Task.Delay(7, ct);

            // Switch to RX
            _port.RtsEnable = false;

            var response = await ReadBytesAsync(expectedBytes, ct);
            return response;
        }
        catch (OperationCanceledException) { return null; }
        catch (TimeoutException)           { return null; }
        catch (InvalidOperationException)  { return null; }
    }

    private async Task<byte[]?> ReadBytesAsync(int count, CancellationToken ct)
    {
        var buf = new byte[count];
        int received = 0;
        var deadline = DateTime.UtcNow.AddMilliseconds(ResponseTimeoutMs);

        while (received < count)
        {
            ct.ThrowIfCancellationRequested();

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"VFD response timeout after {ResponseTimeoutMs} ms");

            if (_port!.BytesToRead > 0)
            {
                received += _port.Read(buf, received, count - received);
            }
            else
            {
                await Task.Delay(5, ct);
            }
        }

        return buf;
    }

    // ────────────────────────────────────────────────────────────
    // Frame builder
    // ────────────────────────────────────────────────────────────

    private byte[] BuildFrame(byte funcCode, byte[] data)
    {
        // [SlaveAddr][FuncCode][DataLen][Data...][CRC_Lo][CRC_Hi]
        var frameData = new byte[3 + data.Length];
        frameData[0] = _slaveAddr;
        frameData[1] = funcCode;
        frameData[2] = (byte)data.Length;
        Array.Copy(data, 0, frameData, 3, data.Length);

        ushort crc = CalcCrc16(frameData, frameData.Length);
        var frame = new byte[frameData.Length + 2];
        Array.Copy(frameData, frame, frameData.Length);
        frame[^2] = (byte)(crc & 0xFF);  // CRC low byte first
        frame[^1] = (byte)(crc >> 8);    // CRC high byte

        return frame;
    }

    /// <summary>
    /// CRC-16/Modbus: polynomial 0xA001, init 0xFFFF.
    /// Applied to all payload bytes; result is lo-byte first, hi-byte second.
    /// </summary>
    private static ushort CalcCrc16(byte[] data, int length)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < length; i++)
        {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0
                    ? (ushort)((crc >> 1) ^ 0xA001)
                    : (ushort)(crc >> 1);
        }
        return crc;
    }

    public void Close()
    {
        if (_port is { IsOpen: true })
        {
            try { _port.RtsEnable = false; _port.Close(); } catch { /* ignore */ }
        }
        _port?.Dispose();
        _port = null;
    }

    public void Dispose() => Close();
}
