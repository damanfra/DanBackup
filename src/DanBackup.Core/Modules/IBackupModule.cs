using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

public interface IBackupModule
{
    string Id { get; }
    string Name { get; }
    string Description { get; }

    /// <summary>Avisos exibidos ao usuário (ex.: "feche o navegador antes").</summary>
    string? Notes { get; }

    bool BackupRequiresAdmin { get; }
    bool RestoreRequiresAdmin { get; }

    /// <summary>Procura neste computador o que pode ser salvo.</summary>
    Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct);

    Task BeforeBackupAsync(IReadOnlyList<BackupItem> items, OperationContext ctx, CancellationToken ct);
    Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct);

    /// <summary>O que pode ser restaurado a partir de um backup (pode ser mais detalhado que os itens salvos).</summary>
    Task<IReadOnlyList<BackupItem>> GetRestoreItemsAsync(ModuleManifest manifest, string moduleDir, CancellationToken ct);

    Task BeforeRestoreAsync(IReadOnlyList<BackupItem> items, OperationContext ctx, CancellationToken ct);
    Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct);

    /// <summary>
    /// Verificação específica do módulo, sem alterar nada no sistema (ex.: o perfil Wi-Fi é um XML válido?).
    /// Integridade (checksums) e descriptografia já são conferidas pelo VerifyEngine antes.
    /// Lança exceção se o item não for recuperável; usa ctx.Warn para ressalvas.
    /// </summary>
    Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct);
}

public abstract class BackupModuleBase : IBackupModule
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual string? Notes => null;
    public virtual bool BackupRequiresAdmin => false;
    public virtual bool RestoreRequiresAdmin => BackupRequiresAdmin;

    public abstract Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct);

    public virtual Task BeforeBackupAsync(IReadOnlyList<BackupItem> items, OperationContext ctx, CancellationToken ct) => Task.CompletedTask;

    public abstract Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct);

    /// <summary>Padrão: os itens que foram salvos com sucesso (ou com avisos).</summary>
    public virtual Task<IReadOnlyList<BackupItem>> GetRestoreItemsAsync(ModuleManifest manifest, string moduleDir, CancellationToken ct)
    {
        IReadOnlyList<BackupItem> items = manifest.Items
            .Where(i => i.Status is ItemStatus.Success or ItemStatus.Warning)
            .Select(i => i.Clone())
            .ToList();
        return Task.FromResult(items);
    }

    public virtual Task BeforeRestoreAsync(IReadOnlyList<BackupItem> items, OperationContext ctx, CancellationToken ct) => Task.CompletedTask;

    public abstract Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct);

    public virtual Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct) => Task.CompletedTask;
}
