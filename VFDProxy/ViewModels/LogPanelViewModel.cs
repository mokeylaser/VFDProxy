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
    /// ALWAYS uses BeginInvoke (async dispatch) regardless of calling thread.
    /// This prevents two classes of crashes:
    ///   1. Cross-thread ObservableCollection modification
    ///   2. CollectionChanged reentrancy during WPF layout passes when multiple
    ///      entries are added synchronously (e.g., during Connect which fires
    ///      5+ log entries in one execution frame)
    /// </summary>
    public void Add(LogEntry entry)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        dispatcher.BeginInvoke(DispatcherPriority.Background, () => AddCore(entry));
    }

    private void AddCore(LogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxLogLines)
            Entries.RemoveAt(0);
    }
}
