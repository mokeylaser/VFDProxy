using System.Windows;
using System.Windows.Threading;

namespace VFDProxy.Infrastructure;

public static class DispatcherService
{
    public static void Invoke(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}
