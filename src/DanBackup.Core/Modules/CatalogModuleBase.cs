using DanBackup.Core.Catalog;
using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Model;

namespace DanBackup.Core.Modules;

/// <summary>
/// Base para módulos cujos itens são listas de caminhos + chaves de registro (apps, jogos).
/// Os caminhos ficam em Data["paths"] e Data["registry"], separados por '|';
/// Data["sensitive"] = "true" faz o item ser salvo criptografado.
/// </summary>
public abstract class CatalogModuleBase : BackupModuleBase
{
    protected const char Separator = '|';

    protected static BackupItem CreateItem(string id, string name, string? description,
        IEnumerable<string> paths, IEnumerable<string> registry, bool selected = true, bool sensitive = false)
    {
        var item = new BackupItem
        {
            Id = id,
            DisplayName = name,
            Description = sensitive ? $"🔒 criptografado · {description}" : description,
            SelectedByDefault = selected,
            Data =
            {
                ["paths"] = string.Join(Separator, paths),
                ["registry"] = string.Join(Separator, registry),
            },
        };
        if (sensitive) item.Data["sensitive"] = "true";
        return item;
    }

    protected static BackupItem? FromCatalogEntry(CatalogEntry entry)
    {
        var existing = entry.Paths.Where(p => ResolveExisting(p).Any()).ToList();
        var registry = entry.Registry.Where(Platform.RegistryHelper.KeyExists).ToList();
        if (existing.Count == 0 && registry.Count == 0) return null;
        var where = existing.Select(PathTokens.Expand).FirstOrDefault() ?? registry.FirstOrDefault();
        return CreateItem(entry.Id, entry.Name, entry.Notes is null ? where : $"{entry.Notes} · {where}",
            existing, registry, sensitive: entry.Sensitive);
    }

    protected static IEnumerable<string> ResolveExisting(string pattern)
    {
        var expanded = PathTokens.Expand(pattern);
        return expanded is null ? [] : GlobResolver.Resolve(expanded);
    }

    protected static IEnumerable<string> Split(string? value) =>
        (value ?? "").Split(Separator, StringSplitOptions.RemoveEmptyEntries);

    protected virtual CopyOptions GetDirectoryOptions(OperationContext ctx) => CopyOptions.Everything;

    public override async Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        await PathSetCopier.BackupAsync(Split(item.Get("paths")), Split(item.Get("registry")),
            ctx.GetItemDir(item), GetDirectoryOptions(ctx), ctx, ct, sensitive: item.Get("sensitive") == "true");
    }

    public override Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct) =>
        PathSetCopier.RestoreAsync(ctx.GetItemDir(item), ctx, ct);

    /// <summary>Confere se os .reg exportados são importáveis.</summary>
    public override Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var regDir = Path.Combine(ctx.GetItemDir(item), PathSetCopier.RegistryDir);
        if (!Directory.Exists(regDir)) return Task.CompletedTask;
        foreach (var file in Directory.EnumerateFiles(regDir))
        {
            var plain = file.EndsWith(Security.BackupCrypto.EncryptedExtension, StringComparison.OrdinalIgnoreCase)
                ? file[..^Security.BackupCrypto.EncryptedExtension.Length]
                : file;
            var content = SecureFile.ReadAllBytes(plain, ctx);
            if (content is null || !SecureFile.IsValidRegFile(content))
                throw new InvalidDataException($"Arquivo de registro inválido: {Path.GetFileName(file)}");
        }
        return Task.CompletedTask;
    }
}
