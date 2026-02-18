using System.Threading.Channels;
using VFDProxy.Drivers;
using VFDProxy.Models;
using VFDProxy.Parsing;

namespace VFDProxy.Engine;

/// <summary>
/// Central coordinator for the VFD Proxy.
///
/// Threading model:
///   - _proxyLoop: reads lines from VirtualComDriver, parses, routes
///   - _vfdLoop:   drains the VFD command channel sequentially (decoupled from proxy loop)
///   - _pollTimer: periodic VFD status reads
///   - All event handlers on engine events must marshal to UI thread before touching bound collections.
/// </summary>
public sealed class ProxyEngine : IProxyEngine
{
    private readonly IVirtualComDriver _virtual;
    private readonly IGrblDriver       _grbl;
    private readonly VfdDriver         _vfd;

    private AppConfig? _config;

    private CancellationTokenSource? _engineCts;
    private Task?                    _vfdLoopTask;

    // VFD command dispatch channel — decouples VFD latency from GRBL pipe
    private Channel<VfdCommand>? _vfdChannel;

    private ProxyState _state = ProxyState.Disconnected;

    public ProxyState State => _state;

    public event EventHandler<StateChangedEventArgs>?    StateChanged;
    public event EventHandler<LogMessageEventArgs>?      LogMessage;
    public event EventHandler<VfdStatusUpdatedEventArgs>? VfdStatusUpdated;

    public ProxyEngine(IVirtualComDriver virtualCom, IGrblDriver grbl, VfdDriver vfd)
    {
        _virtual = virtualCom;
        _grbl    = grbl;
        _vfd     = vfd;
    }

    // ────────────────────────────────────────────────────────────
    // Lifecycle
    // ────────────────────────────────────────────────────────────

    public async Task StartAsync(AppConfig config, CancellationToken ct = default)
    {
        if (_state is ProxyState.Running or ProxyState.Connecting) return;

        _config = config;
        SetState(ProxyState.Connecting);
        Log(LogEntry.Info("Connecting to ports..."));

        try
        {
            _vfd.Configure(config.VfdSlaveAddr);

            await _virtual.OpenAsync(config.VirtualPortProxy, config.GrblBaud, ct);
            Log(LogEntry.Info($"Virtual COM open: {config.VirtualPortProxy}"));

            await _grbl.OpenAsync(config.GrblPort, config.GrblBaud, ct);
            Log(LogEntry.Info($"GRBL COM open: {config.GrblPort} @ {config.GrblBaud}"));

            await _vfd.OpenAsync(config.VfdPort, config.VfdBaud, ct);
            Log(LogEntry.Info($"VFD RS-485 open: {config.VfdPort} @ {config.VfdBaud}"));
        }
        catch (Exception ex)
        {
            CloseAllPorts();
            SetState(ProxyState.Error, ex.Message);
            Log(LogEntry.Error($"Connection failed: {ex.Message}"));
            return;
        }

        _engineCts  = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _vfdChannel = Channel.CreateUnbounded<VfdCommand>(new UnboundedChannelOptions { SingleReader = true });

        // Wire GRBL responses → Virtual COM (so Candle sees them)
        _grbl.ResponseReceived += OnGrblResponse;

        _vfdLoopTask = Task.Run(() => VfdLoopAsync(_engineCts.Token));

        // Start the proxy reader loop
        _ = Task.Run(() => ProxyLoopAsync(_engineCts.Token));

        // Start status poll
        _ = Task.Run(() => PollVfdStatusAsync(_engineCts.Token));

        SetState(ProxyState.Running);
        Log(LogEntry.Info("Proxy running. Candle can now connect."));
    }

    public async Task StopAsync()
    {
        if (_state == ProxyState.Disconnected) return;
        Log(LogEntry.Info("Stopping proxy..."));
        await TeardownAsync(graceful: true);
        SetState(ProxyState.Disconnected);
        Log(LogEntry.Info("Proxy stopped."));
    }

    public async Task EmergencyStopAsync()
    {
        Log(LogEntry.Warn("EMERGENCY STOP triggered."));
        await TeardownAsync(graceful: false);
        SetState(ProxyState.Disconnected);
    }

    private async Task TeardownAsync(bool graceful)
    {
        _engineCts?.Cancel();

        // Unconditional VFD stop — try/finally guarantees it runs
        try
        {
            using var stopCts = new CancellationTokenSource(1500);
            await _vfd.StopAsync(stopCts.Token);
            Log(LogEntry.Info("VFD spindle stopped."));
        }
        catch (Exception ex)
        {
            Log(LogEntry.Warn($"VFD stop error (ignored): {ex.Message}"));
        }
        finally
        {
            CloseAllPorts();
        }

        if (_vfdLoopTask is not null)
        {
            try { await _vfdLoopTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { /* ignore */ }
        }

        _engineCts?.Dispose();
        _engineCts = null;
    }

    private void CloseAllPorts()
    {
        _grbl.ResponseReceived -= OnGrblResponse;
        try { _virtual.Close(); } catch { /* ignore */ }
        try { _grbl.Close();    } catch { /* ignore */ }
        try { _vfd.Close();     } catch { /* ignore */ }
    }

    // ────────────────────────────────────────────────────────────
    // Public spindle control (from UI)
    // ────────────────────────────────────────────────────────────

    public async Task SpindleStopAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(1500);
            await _vfd.StopAsync(cts.Token);
            Log(LogEntry.Info("Spindle stopped (manual)."));
        }
        catch (Exception ex)
        {
            Log(LogEntry.Error($"Spindle stop error: {ex.Message}"));
        }
    }

    public async Task SpindleSetRpmAsync(double rpm, bool cw = true)
    {
        if (_config is null) return;
        double hz = RpmToHz(rpm, _config);
        hz = Math.Clamp(hz, _config.MinHz, _config.MaxHz);

        try
        {
            using var cts = new CancellationTokenSource(2000);
            await _vfd.SetFrequencyAsync(hz, cts.Token);
            if (cw) await _vfd.RunCwAsync(cts.Token);
            else    await _vfd.RunCcwAsync(cts.Token);
            Log(LogEntry.Info($"Spindle set: {rpm:F0} RPM ({hz:F2} Hz) {(cw ? "CW" : "CCW")}"));
        }
        catch (Exception ex)
        {
            Log(LogEntry.Error($"Spindle set error: {ex.Message}"));
        }
    }

    // ────────────────────────────────────────────────────────────
    // Proxy reader loop
    // ────────────────────────────────────────────────────────────

    private async Task ProxyLoopAsync(CancellationToken ct)
    {
        // Use a TaskCompletionSource bridging the event-based DataReceived to async/await
        var lineQueue = Channel.CreateUnbounded<string>();
        _virtual.LineReceived += (_, line) => lineQueue.Writer.TryWrite(line);

        try
        {
            await foreach (var rawLine in lineQueue.Reader.ReadAllAsync(ct))
            {
                ct.ThrowIfCancellationRequested();

                Log(LogEntry.Received(rawLine));

                var parsed = GCodeParser.Parse(rawLine, _config!);
                await DispatchLineAsync(parsed, ct);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            Log(LogEntry.Error($"Proxy loop fault: {ex.Message}"));
            SetState(ProxyState.Error, ex.Message);
            _ = TeardownAsync(graceful: false);
        }
    }

    private async Task DispatchLineAsync(ParsedGCodeLine parsed, CancellationToken ct)
    {
        switch (parsed.Routing)
        {
            case LineRouting.InterceptSpindle:
                // Acknowledge immediately — Candle must not wait
                _virtual.WriteResponse("ok");
                EnqueueVfdCommand(parsed);
                break;

            case LineRouting.InterceptToolChange:
                _virtual.WriteResponse("ok");
                Log(LogEntry.Info($"Tool change discarded: {parsed.Raw.Trim()}"));
                break;

            case LineRouting.InterceptPause:
                _virtual.WriteResponse("ok");
                Log(LogEntry.Info($"Pause (M0/M1) acknowledged: {parsed.Raw.Trim()}"));
                break;

            case LineRouting.InterceptCoolant:
                _virtual.WriteResponse("ok");
                Log(LogEntry.Debug($"Coolant command stripped: {parsed.Raw.Trim()}"));
                break;

            case LineRouting.PassThrough:
                // Empty/comment lines — acknowledge without forwarding
                _virtual.WriteResponse("ok");
                break;

            case LineRouting.ForwardToGrbl:
            default:
                // Strip spindle tokens if the line also had motion and spindle words mixed
                var lineToSend = (parsed.HasM3 || parsed.HasM4 || parsed.HasM5 || parsed.HasSWord)
                    ? GCodeParser.StripSpindleTokens(parsed.Normalized)
                    : parsed.Normalized;

                if (!string.IsNullOrWhiteSpace(lineToSend))
                {
                    Log(LogEntry.Sent(lineToSend));
                    await _grbl.SendLineAsync(lineToSend, ct);
                    // Note: GRBL's "ok" is forwarded to Candle by OnGrblResponse
                }
                else
                {
                    // After stripping, nothing left — still ack
                    _virtual.WriteResponse("ok");
                }

                // Also queue VFD command if the line had spindle words (mixed line)
                if (parsed.Routing == LineRouting.ForwardToGrbl &&
                    (parsed.HasM3 || parsed.HasM4 || parsed.HasM5 || parsed.HasSWord))
                {
                    EnqueueVfdCommand(parsed);
                }
                break;
        }
    }

    private void EnqueueVfdCommand(ParsedGCodeLine parsed)
    {
        _vfdChannel?.Writer.TryWrite(new VfdCommand(parsed, _config!));
    }

    // ────────────────────────────────────────────────────────────
    // VFD dispatch loop (sequential, isolated from GRBL pipe)
    // ────────────────────────────────────────────────────────────

    private async Task VfdLoopAsync(CancellationToken ct)
    {
        if (_vfdChannel is null) return;

        await foreach (var cmd in _vfdChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await ExecuteVfdCommandAsync(cmd, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log(LogEntry.Error($"VFD command error: {ex.Message}"));
            }
        }
    }

    private async Task ExecuteVfdCommandAsync(VfdCommand cmd, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(2000);
        var tok = timeout.Token;

        // M5 = stop
        if (cmd.Parsed.HasM5)
        {
            await _vfd.StopAsync(tok);
            Log(LogEntry.Info("VFD: STOP (M5)"));
            return;
        }

        // Set frequency first (required before run)
        if (cmd.Parsed.HasSWord && cmd.Parsed.SpindleRpm > 0)
        {
            double hz = Math.Clamp(
                RpmToHz(cmd.Parsed.SpindleRpm, cmd.Config),
                cmd.Config.MinHz,
                cmd.Config.MaxHz);
            await _vfd.SetFrequencyAsync(hz, tok);
            Log(LogEntry.Info($"VFD: Set {cmd.Parsed.SpindleRpm:F0} RPM → {hz:F2} Hz"));
        }

        // Direction
        if (cmd.Parsed.HasM3)
        {
            await _vfd.RunCwAsync(tok);
            Log(LogEntry.Info("VFD: RUN CW (M3)"));
        }
        else if (cmd.Parsed.HasM4)
        {
            if (cmd.Config.M4IsCcw)
            {
                await _vfd.RunCcwAsync(tok);
                Log(LogEntry.Info("VFD: RUN CCW (M4)"));
            }
            else
            {
                await _vfd.RunCwAsync(tok);
                Log(LogEntry.Warn("VFD: M4 treated as CW (CCW disabled in config)"));
            }
        }
        else if (cmd.Parsed.HasSWord && cmd.Parsed.SpindleRpm > 0)
        {
            // S word alone (no M3/M4): update frequency without changing run state
            Log(LogEntry.Info($"VFD: Frequency updated (S{cmd.Parsed.SpindleRpm:F0}, no direction change)"));
        }
    }

    // ────────────────────────────────────────────────────────────
    // VFD status polling
    // ────────────────────────────────────────────────────────────

    private async Task PollVfdStatusAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var status = await _vfd.ReadStatusAsync(ct);
                VfdStatusUpdated?.Invoke(this, new VfdStatusUpdatedEventArgs(status));
            }
            catch (OperationCanceledException) { break; }
            catch { /* status poll failure is non-fatal */ }
        }
    }

    // ────────────────────────────────────────────────────────────
    // GRBL → Virtual COM bridge
    // ────────────────────────────────────────────────────────────

    private void OnGrblResponse(object? sender, string line)
    {
        _virtual.WriteResponse(line);
        Log(LogEntry.Received($"GRBL: {line}"));
    }

    // ────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────

    private void SetState(ProxyState newState, string? error = null)
    {
        var prev = _state;
        _state = newState;
        StateChanged?.Invoke(this, new StateChangedEventArgs(prev, newState, error));
    }

    private void Log(LogEntry entry) =>
        LogMessage?.Invoke(this, new LogMessageEventArgs(entry));

    private static double RpmToHz(double rpm, AppConfig config) =>
        rpm * config.PolePairs / 60.0;
}

// ────────────────────────────────────────────────────────────────
// VFD command record (queued to VFD channel)
// ────────────────────────────────────────────────────────────────

internal sealed record VfdCommand(ParsedGCodeLine Parsed, AppConfig Config);
