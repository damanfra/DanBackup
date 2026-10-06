using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

/// <summary>Chaves do registro escolhidas pelo usuário nas Configurações.</summary>
public sealed class RegistryKeysModule : BackupModuleBase
{
    public override string Id => "registry";
    public override string Name => "Chaves do registro";
    public override string Description => "Chaves do registro que você adicionar nas Configurações (exportadas como .reg).";

    public override Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        IReadOnlyList<BackupItem> items = settings.CustomRegistryKeys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(RegistryHelper.KeyExists)
            .Select(key => new BackupItem
            {
                Id = PathUtil.SafeName(key.Replace('\\', '_')),
                DisplayName = key,
                Description = RegistryHelper.IsMachineWide(key) ? "Restaurar exige administrador." : null,
                Data = { ["key"] = key },
            })
            .ToList();
        return Task.FromResult(items);
    }

    public override Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct) =>
        RegistryHelper.ExportAsync(item.Get("key")!, Path.Combine(ctx.GetItemDir(item), "chave.reg"), ct);

    public override Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var content = SecureFile.ReadAllBytes(Path.Combine(ctx.GetItemDir(item), "chave.reg"), ctx);
        if (content is null || !SecureFile.IsValidRegFile(content))
            throw new InvalidDataException("Arquivo .reg ausente ou inválido.");
        return Task.CompletedTask;
    }

    public override Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        if (RegistryHelper.IsMachineWide(item.Get("key")!) && !SystemInfo.IsAdministrator())
            throw new UnauthorizedAccessException("Esta chave (HKLM) só pode ser restaurada como administrador.");
        return RegistryHelper.ImportAsync(Path.Combine(ctx.GetItemDir(item), "chave.reg"), ct);
    }
}
