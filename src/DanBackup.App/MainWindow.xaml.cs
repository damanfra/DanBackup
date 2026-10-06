using System.Windows;
using DanBackup.App.ViewModels;

namespace DanBackup.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
