using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Modules;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;

namespace DanBackup.App.ViewModels;

/// <summary>
/// Restauração: nada vem marcado e tudo passa por confirmação. Programas só são instalados se marcados um a um.
/// </summary>
public sealed partial class RestoreViewModel : OperationViewModelBase
{
    private readonly AppSettings _settings;
    private readonly bool _isAdmin;

    public RestoreViewModel(AppSettings settings, bool isAdmin)
    {
        _settings = settings;
        _isAdmin = isAdmin;
        LoadRememberedPassword(settings);
        StatusText = "Escolha a pasta de um backup (a que contém o arquivo manifest.json).";
    }

    private BackupManifest? _manifest;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBackupFolderCommand))]
    private string? _backupFolder;

    [ObservableProperty]
    private string? _manifestSummary;

    /// <summary>O backup carregado tem itens criptografados (exige a senha).</summary>
    [ObservableProperty]
    private bool _isEncrypted;

    protected override void OnBusyChanged() => RestoreCommand.NotifyCanExecuteChanged();

    protected override void OnSelectionChanged() => RestoreCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task BrowseBackupAsync()
    {
        var folder = BackupFolderPicker.Pick(BackupFolder ?? _settings.LastDestination);
        if (folder is null) return;
        BackupFolder = folder;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Modules.Clear();
        var folder = BackupFolder!;
        var manifest = BackupFolderPicker.Load(folder);
        if (manifest is null) return;

        _manifest = manifest;
        IsEncrypted = manifest.Encryption is not null;
        ManifestSummary = BackupFolderPicker.Describe(manifest);

        var registry = ModuleRegistry.CreateAll().ToDictionary(m => m.Id);
        var found = new List<(IBackupModule Module, IReadOnlyList<BackupItem> Items)>();
        IOperationReporter reporter = this;

        await RunBusyAsync("Lendo o backup...", async ct =>
        {
            foreach (var mm in manifest.Modules)
            {
                if (!registry.TryGetValue(mm.Id, out var module))
                {
                    reporter.Log(LogLevel.Warning, $"Módulo desconhecido no backup: {mm.Name}");
                    continue;
                }
                try
                {
                    var items = await module.GetRestoreItemsAsync(mm, Path.Combine(folder, "modules", mm.Id), ct);
                    if (items.Count > 0) found.Add((module, items));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    reporter.Log(LogLevel.Error, $"{module.Name}: {ex.Message}");
                }
            }
        });

        foreach (var (module, items) in found)
        {
            bool needsAdmin = module.RestoreRequiresAdmin && !_isAdmin;
            // Na restauração nada vem marcado.
            Modules.Add(new ModuleNode(module, items, !needsAdmin, needsAdmin, _ => false, UpdateSelectionSummary));
        }
        UpdateSelectionSummary();
        Progress = 0;
        StatusText = "Marque o que deseja restaurar. Nada é restaurado sem a sua confirmação.";
    }

    private bool CanRestore() => !IsBusy && BackupFolder is not null && Modules.Any(m => m.Items.Any(i => i.IsChecked));

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        var plan = BuildPlan();
        if (plan.Count == 0 || _manifest is null) return;

        // Confere a senha antes de pedir confirmação, para não começar e falhar no meio.
        if (IsEncrypted)
        {
            try
            {
                RestoreEngine.OpenCrypto(_manifest, Password);
            }
            catch (InvalidPasswordException)
            {
                Dialogs.Warning(string.IsNullOrEmpty(Password)
                    ? "Este backup tem itens criptografados. Digite a senha usada no backup."
                    : "Senha incorreta para este backup.");
                return;
            }
            PersistPasswordChoice(_settings);
        }

        var message = new StringBuilder("Restaurar os itens selecionados?\n\n");
        foreach (var p in plan)
            message.AppendLine($"• {p.Module.Name}: {p.Items.Count} item(ns)");
        if (plan.Any(p => p.Module is ProgramsModule))
            message.AppendLine("\nOs programas marcados serão INSTALADOS via winget (alguns podem pedir confirmação do Windows).");
        message.AppendLine("\nArquivos existentes com o mesmo nome serão sobrescritos.");
        if (!Dialogs.Confirm(message.ToString())) return;

        foreach (var item in Modules.SelectMany(m => m.Items)) item.ResetResult();
        var folder = BackupFolder!;
        var password = Password;

        bool completed = await RunBusyAsync("Restaurando...", ct => new RestoreEngine(_settings, this, password).RunAsync(folder, plan, ct));

        ApplyResults(plan.Select(p => (p.Module.Id, p.Items)));
        if (!completed) return;
        var summary = Summarize(plan.SelectMany(p => p.Items));
        StatusText = $"Restauração concluída: {summary}.";
        Dialogs.Info($"Restauração concluída: {summary}.\n\nPasse o mouse sobre os itens com aviso/erro para ver o motivo.\nLogs em {RestoreEngine.LogsDir}");
    }

    private bool CanOpenBackupFolder() => BackupFolder is not null;

    [RelayCommand(CanExecute = nameof(CanOpenBackupFolder))]
    private void OpenBackupFolder() => Dialogs.OpenInExplorer(BackupFolder!);
}
