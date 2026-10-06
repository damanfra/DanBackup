using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Modules;
using DanBackup.Core.Settings;

namespace DanBackup.App.ViewModels;

public sealed partial class BackupViewModel : OperationViewModelBase
{
    private readonly AppSettings _settings;
    private readonly bool _isAdmin;

    public BackupViewModel(AppSettings settings, bool isAdmin)
    {
        _settings = settings;
        _isAdmin = isAdmin;
        _destinationFolder = settings.LastDestination ?? "";
        _backupName = DefaultBackupName();
        LoadRememberedPassword(settings);
        StatusText = "Escolha o destino e clique em \"Analisar este computador\".";
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBackupCommand))]
    private string _destinationFolder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBackupCommand))]
    private string _backupName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBackupFolderCommand))]
    private string? _lastBackupRoot;

    private static string DefaultBackupName() => $"DanBackup_{Environment.MachineName}_{DateTime.Now:yyyy-MM-dd_HHmm}";

    protected override void OnBusyChanged()
    {
        AnalyzeCommand.NotifyCanExecuteChanged();
        StartBackupCommand.NotifyCanExecuteChanged();
    }

    protected override void OnSelectionChanged() => StartBackupCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void BrowseDestination()
    {
        var folder = Dialogs.PickFolder("Onde salvar o backup (HD externo, pendrive, pasta do Google Drive...)", DestinationFolder);
        if (folder is not null) DestinationFolder = folder;
    }

    private bool CanAnalyze() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        Modules.Clear();
        var found = new List<(IBackupModule Module, IReadOnlyList<BackupItem> Items)>();
        IOperationReporter reporter = this;
        await RunBusyAsync("Analisando este computador...", async ct =>
        {
            var modules = ModuleRegistry.CreateAll();
            for (int i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                reporter.Status($"Analisando: {module.Name}...");
                reporter.Progress(i, modules.Count);
                try
                {
                    var items = await module.DiscoverAsync(_settings, ct);
                    if (items.Count > 0) found.Add((module, items));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    reporter.Log(LogLevel.Error, $"{module.Name}: falha ao analisar ({ex.Message})");
                }
            }
        });

        foreach (var (module, items) in found)
        {
            bool needsAdmin = module.BackupRequiresAdmin && !_isAdmin;
            Modules.Add(new ModuleNode(module, items, !needsAdmin, needsAdmin, item => item.SelectedByDefault, UpdateSelectionSummary));
        }
        UpdateSelectionSummary();
        Progress = 0;
        if (!IsBusy && StatusText != "Cancelado.")
            StatusText = $"{Modules.Sum(m => m.Items.Count)} itens encontrados em {Modules.Count} categorias. Revise a seleção e inicie o backup.";
    }

    private bool CanStart() =>
        !IsBusy && !string.IsNullOrWhiteSpace(DestinationFolder) && !string.IsNullOrWhiteSpace(BackupName) &&
        Password.Length >= MinPasswordLength && Modules.Any(m => m.Items.Any(i => i.IsChecked));

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartBackupAsync()
    {
        if (!Directory.Exists(DestinationFolder))
        {
            Dialogs.Warning($"A pasta de destino não existe:\n{DestinationFolder}");
            return;
        }

        var root = Path.Combine(DestinationFolder, PathUtil.SafeName(BackupName));
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            if (!Dialogs.Confirm($"A pasta {root} já existe e não está vazia.\nArquivos com o mesmo nome serão sobrescritos. Continuar?"))
                return;
        }

        _settings.LastDestination = DestinationFolder;
        PersistPasswordChoice(_settings);

        foreach (var item in Modules.SelectMany(m => m.Items)) item.ResetResult();
        var plan = BuildPlan();
        BackupManifest? manifest = null;
        LastBackupRoot = root;
        var password = Password;

        await RunBusyAsync("Iniciando backup...", async ct =>
        {
            var engine = new BackupEngine(_settings, this, password);
            try
            {
                manifest = await engine.RunAsync(root, plan, ct);
            }
            catch (OperationCanceledException) when (BackupManifest.Exists(root))
            {
                manifest = BackupManifest.Load(root);
                throw;
            }
        });

        if (manifest is null) return;
        ApplyResults(manifest.Modules.Select(m => (m.Id, (IReadOnlyList<BackupItem>)m.Items)));
        var summary = Summarize(manifest.Modules.SelectMany(m => m.Items));
        StatusText = manifest.Cancelled ? $"Backup cancelado ({summary})." : $"Backup concluído: {summary}.";
        BackupName = DefaultBackupName();
        if (!manifest.Cancelled &&
            Dialogs.Confirm($"Backup concluído em:\n{root}\n\n{summary}.\n\nPasse o mouse sobre os itens com aviso/erro para ver o motivo.\n\n" +
                            "Deseja verificar agora se o backup pode ser restaurado? (recomendado antes de formatar)"))
        {
            VerifyRequested?.Invoke(root);
        }
    }

    /// <summary>Disparado quando o usuário pede para verificar o backup recém-criado.</summary>
    public event Action<string>? VerifyRequested;

    private bool CanOpenBackupFolder() => LastBackupRoot is not null;

    [RelayCommand(CanExecute = nameof(CanOpenBackupFolder))]
    private void OpenBackupFolder() => Dialogs.OpenInExplorer(LastBackupRoot!);
}
