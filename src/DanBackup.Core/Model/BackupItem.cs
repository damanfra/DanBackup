namespace DanBackup.Core.Model;

public enum ItemStatus
{
    Pending,
    Success,
    Warning,
    Failed,
    Skipped,
}

/// <summary>
/// Algo que pode ser salvo ou restaurado por um módulo (uma pasta, um perfil de navegador, uma rede Wi-Fi...).
/// </summary>
public sealed class BackupItem
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public bool SelectedByDefault { get; init; } = true;

    /// <summary>Dados específicos do módulo (caminhos, nomes de perfis, etc.). Persistidos no manifesto.</summary>
    public Dictionary<string, string> Data { get; init; } = new();

    public ItemStatus Status { get; set; } = ItemStatus.Pending;
    public string? Message { get; set; }

    public string? Get(string key) => Data.TryGetValue(key, out var v) ? v : null;

    public BackupItem Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Description = Description,
        SelectedByDefault = SelectedByDefault,
        Data = new Dictionary<string, string>(Data),
    };
}
