using System.Windows;
using System.Windows.Controls;
using VFDProxy.ViewModels;

namespace VFDProxy.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainWindowViewModel();
        DataContext = _vm;

        // Auto-scroll log to bottom on new items
        _vm.Log.Entries.CollectionChanged += (_, _) =>
        {
            if (LogListView.Items.Count > 0)
                LogListView.ScrollIntoView(LogListView.Items[^1]);
        };
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _vm.OnWindowClosing();
    }
}
