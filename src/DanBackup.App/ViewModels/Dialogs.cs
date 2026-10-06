using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace DanBackup.App.ViewModels;

public static class Dialogs
{
    private const string Title = "DanBackup";

    public static string? PickFolder(string title, string? initial = null)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial))
            dialog.InitialDirectory = initial;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public static bool Confirm(string message) =>
        MessageBox.Show(message, Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public static void Info(string message) =>
        MessageBox.Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Warning(string message) =>
        MessageBox.Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Error(string message) =>
        MessageBox.Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Error);

    public static void OpenInExplorer(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
