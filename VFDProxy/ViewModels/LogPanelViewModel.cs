using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VFDProxy.Infrastructure;
using VFDProxy.Models;

namespace VFDProxy.ViewModels;

public sealed class LogPanelViewModel : ViewModelBase
{
    private int _maxLogLines = 2000;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public ICommand ClearCommand { get; }

    public int MaxLogLines
    {
        get => _maxLogLines;
        set => SetField(ref _maxLogLines, value);
    }

    public LogPanelViewModel()
    {
        ClearCommand = new RelayCommand(() =>
        {
            var d = Application.Current?.Dispatcher;
            if (d is null) return;
            if (d.CheckAccess()) Entries.Clear();
            else d.BeginInvoke(Entries.Clear);
        });
    }

    /// <summary>
    /// Thread-safe log entry addition.
    /// Uses BeginInvoke (async) instead of Invoke (sync) to prevent reentrancy
    /// during WPF layout passes, which causes "ItemsControl inconsistent with
    /// its items source" crashes in VirtualizingStackPanel.
    /// </summary>
    public void Add(LogEntry entry)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        if (dispatcher.CheckAccess())
        {
            AddCore(entry);
        }
        else
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, () => AddCore(entry));
        }
    }

    private void AddCore(LogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxLogLines)
            Entries.RemoveAt(0);
    }
}
