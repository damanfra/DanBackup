using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;

namespace DanBackup.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int VerifyTab = 1;

    public MainViewModel()
    {
        var settings = SettingsStore.Load();
        IsAdmin = SystemInfo.IsAdministrator();
        Backup = new BackupViewModel(settings, IsAdmin);
        Verify = new VerifyViewModel(settings);
        Restore = new RestoreViewModel(settings, IsAdmin);
        Settings = new SettingsViewModel(settings);

        // Ao fim do backup, o usuário pode ir direto para a verificação.
        Backup.VerifyRequested += folder =>
        {
            Verify.Open(folder);
            Verify.Password = Backup.Password;
            SelectedTabIndex = VerifyTab;
            if (Verify.VerifyCommand.CanExecute(null))
                Verify.VerifyCommand.Execute(null);
        };
    }

    public bool IsAdmin { get; }
    public bool ShowAdminBanner => !IsAdmin;
    public string Title => $"DanBackup {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}" + (IsAdmin ? " (Administrador)" : "");

    public BackupViewModel Backup { get; }
    public VerifyViewModel Verify { get; }
    public RestoreViewModel Restore { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    [RelayCommand]
    private void RestartAsAdmin()
    {
        if (Backup.IsBusy || Verify.IsBusy || Restore.IsBusy)
        {
            Dialogs.Warning("Aguarde a operação atual terminar.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception)
        {
            // Usuário recusou o UAC.
        }
    }
}
