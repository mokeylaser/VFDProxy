using VFDProxy.Models;

namespace VFDProxy.Engine;

public sealed class LogMessageEventArgs(LogEntry entry) : EventArgs
{
    public LogEntry Entry { get; } = entry;
}

public sealed class StateChangedEventArgs(ProxyState previous, ProxyState current, string? errorMessage = null) : EventArgs
{
    public ProxyState Previous     { get; } = previous;
    public ProxyState Current      { get; } = current;
    public string?    ErrorMessage { get; } = errorMessage;
}

public sealed class VfdStatusUpdatedEventArgs(VfdStatus status) : EventArgs
{
    public VfdStatus Status { get; } = status;
}
