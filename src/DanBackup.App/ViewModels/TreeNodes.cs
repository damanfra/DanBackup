using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DanBackup.Core.Model;
using DanBackup.Core.Modules;

namespace DanBackup.App.ViewModels;

/// <summary>Um módulo na árvore de seleção; o checkbox fica indeterminado quando só parte dos itens está marcada.</summary>
public sealed partial class ModuleNode : ObservableObject
{
    private readonly Action? _selectionChanged;
    private bool? _isChecked;
    private bool _updating;

    public ModuleNode(IBackupModule module, IEnumerable<BackupItem> items, bool isAvailable, bool needsAdmin,
        Func<BackupItem, bool> initiallyChecked, Action? selectionChanged)
    {
        Module = module;
        IsAvailable = isAvailable;
        NeedsAdmin = needsAdmin;
        _selectionChanged = selectionChanged;
        foreach (var item in items)
            Items.Add(new ItemNode(this, item, isAvailable && initiallyChecked(item)));
        Recompute();
    }

    public IBackupModule Module { get; }
    public ObservableCollection<ItemNode> Items { get; } = [];
    public bool IsAvailable { get; }
    public bool NeedsAdmin { get; }
    public bool HasNotes => !string.IsNullOrEmpty(Module.Notes);
    public string CountText => Items.Count == 1 ? "(1 item)" : $"({Items.Count} itens)";

    [ObservableProperty]
    private bool _isExpanded;

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (!IsAvailable) return;
            bool target = value ?? true;
            _updating = true;
            foreach (var item in Items) item.IsChecked = target;
            _updating = false;
            Recompute();
        }
    }

    public IEnumerable<BackupItem> CheckedItems => Items.Where(i => i.IsChecked).Select(i => i.Item);

    internal void Recompute()
    {
        if (_updating) return;
        int count = Items.Count(i => i.IsChecked);
        bool? state = count == 0 ? false : count == Items.Count ? true : null;
        SetProperty(ref _isChecked, state, nameof(IsChecked));
        _selectionChanged?.Invoke();
    }
}

public sealed partial class ItemNode : ObservableObject
{
    public ItemNode(ModuleNode parent, BackupItem item, bool isChecked)
    {
        Parent = parent;
        Item = item;
        _isChecked = isChecked;
    }

    public ModuleNode Parent { get; }
    public BackupItem Item { get; }

    // Exigido pelo estilo do TreeViewItem; itens não têm filhos.
    public bool IsExpanded { get; set; }

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private ItemStatus _status = ItemStatus.Pending;

    [ObservableProperty]
    private string? _message;

    partial void OnIsCheckedChanged(bool value) => Parent.Recompute();

    public void ApplyResult(BackupItem result)
    {
        Status = result.Status;
        Message = result.Message;
    }

    public void ResetResult()
    {
        Status = ItemStatus.Pending;
        Message = null;
    }
}
