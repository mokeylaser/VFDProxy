using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace VFDProxy;

public partial class App : Application
{
    // Reentrancy guard — prevents nested MessageBox.Show from creating
    // a recursive message pump that overflows the stack.
    private bool _handlingException;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch unhandled exceptions on the UI thread — log and survive if possible
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Catch unhandled exceptions on background threads
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        // Catch unobserved task exceptions
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Trace.TraceError($"[VFDProxy] UI thread exception: {e.Exception}");
        e.Handled = true;

        // Guard against reentrancy: MessageBox.Show() creates a nested message pump.
        // If the same exception recurs during the nested pump (e.g., layout errors),
        // this handler would re-enter, creating infinite recursion → stack overflow.
        if (_handlingException)
            return;

        _handlingException = true;
        try
        {
            // Show the error asynchronously to avoid nested pump issues
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                try
                {
                    MessageBox.Show(
                        $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe application will try to continue.",
                        "VFDProxy Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    _handlingException = false;
                }
            });
        }
        catch
        {
            _handlingException = false;
        }
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Trace.TraceError($"[VFDProxy] Background thread exception: {ex}");

        if (e.IsTerminating)
        {
            MessageBox.Show(
                $"A fatal error occurred:\n\n{ex?.Message ?? "Unknown error"}\n\nThe application must close.",
                "VFDProxy Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Trace.TraceError($"[VFDProxy] Unobserved task exception: {e.Exception}");
        e.SetObserved(); // Prevent process termination
    }
}
