using System.Windows.Input;

namespace VFDProxy.Infrastructure;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task>    _execute;
    private readonly Func<bool>?   _canExecute;
    private bool                   _isExecuting;

    public event EventHandler? CanExecuteChanged;
    public event EventHandler<Exception>? CommandFailed;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        _isExecuting = true;
        RaiseCanExecuteChanged();

        try   { await _execute(); }
        catch (Exception ex) { CommandFailed?.Invoke(this, ex); }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
