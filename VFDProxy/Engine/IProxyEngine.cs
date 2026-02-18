using VFDProxy.Models;

namespace VFDProxy.Engine;

public interface IProxyEngine
{
    ProxyState State { get; }

    event EventHandler<StateChangedEventArgs>    StateChanged;
    event EventHandler<LogMessageEventArgs>      LogMessage;
    event EventHandler<VfdStatusUpdatedEventArgs> VfdStatusUpdated;

    Task StartAsync(AppConfig config, CancellationToken ct = default);
    Task StopAsync();

    /// <summary>Unconditional safety stop — stops VFD and closes all ports immediately.</summary>
    Task EmergencyStopAsync();

    /// <summary>Direct VFD spindle stop without full engine teardown (for the UI STOP button).</summary>
    Task SpindleStopAsync();

    /// <summary>Manually set spindle to a given RPM (from UI manual controls).</summary>
    Task SpindleSetRpmAsync(double rpm, bool cw = true);
}
