using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Modules;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;

namespace DanBackup.App.ViewModels;

/// <summary>
/// Teste de recuperação ("blind test") de um backup: confere senha, checksums, descriptografia
/// e o conteúdo de cada item, sem alterar nada no sistema.
/// </summary>
public sealed partial class VerifyViewModel : OperationViewModelBase
{
    private readonly AppSettings _settings;
    private BackupManifest? _manifest;

    public VerifyViewModel(AppSettings settings)
    {
        _settings = settings;
        LoadRememberedPassword(settings);
        StatusText = "Escolha um backup para testar se ele pode ser restaurado. Nada é alterado neste computador.";
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBackupFolderCommand))]
    private string? _backupFolder;

    [ObservableProperty]
    private string? _manifestSummary;

    [ObservableProperty]
    private bool _isEncrypted;

    /// <summary>Resultado geral da última verificação (texto destacado na tela).</summary>
    [ObservableProperty]
    private string? _verdict;

    protected override void OnBusyChanged() => VerifyCommand.NotifyCanExecuteChanged();

    protected override void OnSelectionChanged() => VerifyCommand.NotifyCanExecuteChanged();

    /// <summary>Usado pela aba Backup para oferecer a verificação logo após um backup.</summary>
    public void Open(string folder)
    {
        BackupFolder = folder;
        Load();
    }

    [RelayCommand]
    private void BrowseBackup()
    {
        var folder = BackupFolderPicker.Pick(BackupFolder ?? _settings.LastDestination);
        if (folder is null) return;
        BackupFolder = folder;
        Load();
    }

    private void Load()
    {
        Modules.Clear();
        Log.Clear();
        Verdict = null;
        Progress = 0;
        _manifest = BackupFolderPicker.Load(BackupFolder!);
        if (_manifest is null) return;

        IsEncrypted = _manifest.Encryption is not null;
        ManifestSummary = BackupFolderPicker.Describe(_manifest);

        // Verifica os itens do manifesto (o que foi salvo), todos marcados por padrão.
        var registry = ModuleRegistry.CreateAll().ToDictionary(m => m.Id);
        foreach (var mm in _manifest.Modules)
        {
            if (!registry.TryGetValue(mm.Id, out var module) || mm.Items.Count == 0) continue;
            var items = mm.Items.Select(CloneWithStatus).ToList();
            Modules.Add(new ModuleNode(module, items, isAvailable: true, needsAdmin: false, _ => true, UpdateSelectionSummary));
        }
        UpdateSelectionSummary();
        StatusText = "Clique em \"Verificar\". Todos os arquivos serão lidos, então pode demorar em backups grandes.";
    }

    // O status do backup vai junto: itens que falharam no backup já são reportados como não recuperáveis.
    private static BackupItem CloneWithStatus(BackupItem item)
    {
        var clone = item.Clone();
        clone.Status = item.Status;
        clone.Message = item.Message;
        return clone;
    }

    private bool CanVerify() => !IsBusy && _manifest is not null && Modules.Any(m => m.Items.Any(i => i.IsChecked));

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        if (IsEncrypted)
        {
            try
            {
                RestoreEngine.OpenCrypto(_manifest!, Password);
            }
            catch (InvalidPasswordException)
            {
                Dialogs.Warning(string.IsNullOrEmpty(Password)
                    ? "Este backup tem itens criptografados. Digite a senha para testá-los."
                    : "Senha incorreta para este backup. Sem a senha certa, os itens criptografados NÃO poderão ser restaurados.");
                return;
            }
            PersistPasswordChoice(_settings);
        }

        // Cada verificação parte do status original do backup.
        var originals = _manifest!.Modules.SelectMany(m => m.Items.Select(i => (m.Id, Item: i))).ToList();
        var plan = Modules
            .Select(m => new ModulePlan(m.Module, m.CheckedItems
                .Select(i => CloneWithStatus(originals.First(o => o.Id == m.Module.Id && o.Item.Id == i.Id).Item))
                .ToList()))
            .Where(p => p.Items.Count > 0)
            .ToList();

        foreach (var item in Modules.SelectMany(m => m.Items)) item.ResetResult();
        Verdict = null;
        var folder = BackupFolder!;
        var password = Password;

        bool completed = await RunBusyAsync("Verificando...", ct => new VerifyEngine(_settings, this, password).RunAsync(folder, plan, ct));
        ApplyResults(plan.Select(p => (p.Module.Id, p.Items)));
        if (!completed) return;

        var all = plan.SelectMany(p => p.Items).ToList();
        int failed = all.Count(i => i.Status == ItemStatus.Failed);
        int warnings = all.Count(i => i.Status == ItemStatus.Warning);
        Verdict = failed > 0
            ? $"✖ {failed} item(ns) NÃO recuperável(is). Refaça o backup desses itens antes de formatar."
            : warnings > 0
                ? $"⚠ Backup recuperável, com {warnings} ressalva(s) — veja os itens em laranja."
                : "✔ Backup íntegro e recuperável.";
        StatusText = $"Verificação concluída: {Summarize(all)}.";
    }

    private bool CanOpenBackupFolder() => BackupFolder is not null;

    [RelayCommand(CanExecute = nameof(CanOpenBackupFolder))]
    private void OpenBackupFolder() => Dialogs.OpenInExplorer(BackupFolder!);
}
