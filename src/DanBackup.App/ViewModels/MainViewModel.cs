using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;
using DanBackup.Core.Update;

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

        Settings.Saved += Backup.InvalidateAnalysis;

        // Ao fim do backup, o usuário pode ir direto para a verificação.
        Backup.VerifyRequested += folder =>
        {
            Verify.Open(folder);
            Verify.Password = Backup.Password;
            SelectedTabIndex = VerifyTab;
            if (Verify.VerifyCommand.CanExecute(null))
                Verify.VerifyCommand.Execute(null);
        };

        _ = CheckForUpdateAsync();
    }

    private readonly UpdateService _updater = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpdateBanner), nameof(UpdateText))]
    private UpdateInfo? _update;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallUpdate))]
    private bool _isUpdating;

    [ObservableProperty]
    private double _updateProgress;

    public bool ShowUpdateBanner => Update is not null;
    public bool CanInstallUpdate => !IsUpdating;
    public string UpdateText => Update is null ? "" : $"Nova versão disponível: {Update.Version.ToString(3)} (você usa a {UpdateService.CurrentVersion.ToString(3)}).";

    private static string? InstalledExePath =>
        string.Equals(Path.GetFileName(Environment.ProcessPath), UpdateService.AssetName, StringComparison.OrdinalIgnoreCase)
            ? Environment.ProcessPath : null;

    /// <summary>Verifica em segundo plano; falhas (sem internet, limite da API) são ignoradas.</summary>
    private async Task CheckForUpdateAsync()
    {
        if (InstalledExePath is not { } exe) return; // rodando via dotnet run / depuração
        UpdateService.CleanupOldVersion(exe);
        try { Update = await _updater.CheckAsync(); }
        catch (Exception) { }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (Update is not { } update || InstalledExePath is not { } exe) return;
        if (Backup.IsBusy || Verify.IsBusy || Restore.IsBusy)
        {
            Dialogs.Warning("Aguarde a operação atual terminar antes de atualizar.");
            return;
        }
        IsUpdating = true;
        try
        {
            var progress = new Progress<double>(p => UpdateProgress = p * 100);
            await _updater.InstallAsync(update, exe, progress);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Não foi possível atualizar:\n\n{ex.Message}\n\nBaixe manualmente em {update.PageUrl}");
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private void DismissUpdate() => Update = null;

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
