using System.Windows;
using TelemetryDash.ViewModels;

namespace TelemetryDash;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
