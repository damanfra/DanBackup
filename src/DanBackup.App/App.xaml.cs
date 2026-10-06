using System.Windows;
using System.Windows.Threading;

namespace DanBackup.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;
        base.OnStartup(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Erro inesperado:\n\n{e.Exception.Message}", "DanBackup", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
