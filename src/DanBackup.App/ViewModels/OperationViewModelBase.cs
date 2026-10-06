using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;

namespace DanBackup.App.ViewModels;

public sealed record LogLine(DateTime Time, LogLevel Level, string Message)
{
    public string Text => $"{Time:HH:mm:ss}  {Message}";
}

/// <summary>Base das abas Backup e Restaurar: árvore de módulos, progresso, log e cancelamento.</summary>
public abstract partial class OperationViewModelBase : ObservableObject, IOperationReporter
{
    private const int MaxLogLines = 5000;
    private CancellationTokenSource? _cts;

    public ObservableCollection<ModuleNode> Modules { get; } = [];
    public ObservableCollection<LogLine> Log { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _selectionSummary = "";

    protected void UpdateSelectionSummary()
    {
        int items = Modules.Sum(m => m.Items.Count(i => i.IsChecked));
        SelectionSummary = items == 0 ? "Nada selecionado" : $"{items} item(ns) selecionado(s)";
        OnSelectionChanged();
    }

    protected virtual void OnSelectionChanged() { }

    public const int MinPasswordLength = 6;

    /// <summary>Senha do backup (o PasswordBox da tela atualiza esta propriedade).</summary>
    [ObservableProperty]
    private string _password = "";

    /// <summary>Guardar a senha neste computador (protegida pelo DPAPI do Windows).</summary>
    [ObservableProperty]
    private bool _rememberPassword;

    protected void LoadRememberedPassword(AppSettings settings)
    {
        var stored = PasswordStore.Load(settings);
        Password = stored ?? "";
        RememberPassword = stored is not null;
    }

    protected void PersistPasswordChoice(AppSettings settings)
    {
        if (RememberPassword) PasswordStore.Remember(settings, Password);
        else PasswordStore.Forget(settings);
        SettingsStore.Save(settings);
    }

    // A senha influencia se os botões de ação podem ser usados.
    partial void OnPasswordChanged(string value) => OnSelectionChanged();

    partial void OnIsBusyChanged(bool value) => OnBusyChanged();

    protected virtual void OnBusyChanged() { }

    protected List<ModulePlan> BuildPlan() =>
        Modules.Where(m => m.IsAvailable)
            .Select(m => new ModulePlan(m.Module, m.CheckedItems.ToList()))
            .Where(p => p.Items.Count > 0)
            .ToList();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var m in Modules) m.IsChecked = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var m in Modules) m.IsChecked = false;
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Cancelando...";
    }

    /// <summary>Executa uma operação longa fora da thread da UI, com cancelamento e tratamento de erro. Retorna true se concluiu.</summary>
    protected async Task<bool> RunBusyAsync(string startStatus, Func<CancellationToken, Task> operation)
    {
        IsBusy = true;
        Progress = 0;
        StatusText = startStatus;
        _cts = new CancellationTokenSource();
        try
        {
            await Task.Run(() => operation(_cts.Token));
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelado.";
            return false;
        }
        catch (Exception ex)
        {
            ((IOperationReporter)this).Log(LogLevel.Error, ex.Message);
            Dialogs.Error(ex.Message);
            StatusText = "Erro.";
            return false;
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    /// <summary>Copia os status do resultado para os nós da árvore.</summary>
    protected void ApplyResults(IEnumerable<(string ModuleId, IReadOnlyList<BackupItem> Items)> results)
    {
        foreach (var (moduleId, items) in results)
        {
            var node = Modules.FirstOrDefault(m => m.Module.Id == moduleId);
            if (node is null) continue;
            foreach (var result in items)
                node.Items.FirstOrDefault(i => i.Item.Id == result.Id)?.ApplyResult(result);
        }
    }

    protected static string Summarize(IEnumerable<BackupItem> items)
    {
        var list = items.ToList();
        int ok = list.Count(i => i.Status == ItemStatus.Success);
        int warn = list.Count(i => i.Status == ItemStatus.Warning);
        int fail = list.Count(i => i.Status == ItemStatus.Failed);
        return $"{ok} ok, {warn} com avisos, {fail} com erro";
    }

    // IOperationReporter — chamado de threads de fundo; repassa para a UI.
    void IOperationReporter.Log(LogLevel level, string message) =>
        OnUi(() =>
        {
            Log.Add(new LogLine(DateTime.Now, level, message));
            if (Log.Count > MaxLogLines) Log.RemoveAt(0);
        });

    void IOperationReporter.Status(string text) => OnUi(() => StatusText = text);

    void IOperationReporter.Progress(int done, int total) =>
        OnUi(() => Progress = total == 0 ? 0 : 100.0 * done / total);

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
