using System.Collections.ObjectModel;
using System.Windows.Input;
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
        ClearCommand = new RelayCommand(() => Entries.Clear());
    }

    public void Add(LogEntry entry)
    {
        DispatcherService.Invoke(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > MaxLogLines)
                Entries.RemoveAt(0);
        });
    }
}
