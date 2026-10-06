using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

/// <summary>Pastas do usuário (Desktop, Documentos...) e pastas personalizadas.</summary>
public sealed class UserFoldersModule : BackupModuleBase
{
    private static readonly (string Token, string Name, bool Default)[] KnownFolders =
    [
        ("DESKTOP", "Área de Trabalho", true),
        ("DOCUMENTS", "Documentos", true),
        ("DOWNLOADS", "Downloads", false),
        ("PICTURES", "Imagens", true),
        ("MUSIC", "Músicas", true),
        ("VIDEOS", "Vídeos", true),
        ("FAVORITES", "Favoritos (Internet Explorer/Edge legado)", false),
    ];

    public override string Id => "user-folders";
    public override string Name => "Pastas do usuário";
    public override string Description => "Área de Trabalho, Documentos, Imagens e as pastas que você adicionar nas Configurações.";

    public override string? Notes =>
        "Arquivos que estão só na nuvem (OneDrive) são ignorados por padrão, porque já estão salvos lá.";

    public override Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        var items = new List<BackupItem>();
        foreach (var (token, name, selected) in KnownFolders)
        {
            var path = PathTokens.Resolve(token);
            if (path is null || !Directory.Exists(path)) continue;
            items.Add(new BackupItem
            {
                Id = token.ToLowerInvariant(),
                DisplayName = name,
                Description = path,
                SelectedByDefault = selected,
                Data = { ["path"] = $"%{token}%" },
            });
        }

        foreach (var folder in settings.CustomFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var expanded = PathTokens.Expand(folder) ?? folder;
            if (!Directory.Exists(expanded)) continue;
            var tokenized = PathTokens.Tokenize(expanded);
            items.Add(new BackupItem
            {
                Id = "custom_" + PathUtil.SafeName(tokenized.Replace('\\', '_').Replace("%", "")),
                DisplayName = $"Pasta: {Path.GetFileName(expanded.TrimEnd('\\'))}",
                Description = expanded,
                Data = { ["path"] = tokenized },
            });
        }

        return Task.FromResult<IReadOnlyList<BackupItem>>(items);
    }

    public override Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var source = PathTokens.Expand(item.Get("path")!)
                     ?? throw new DirectoryNotFoundException($"Pasta não encontrada: {item.Get("path")}");
        var options = new CopyOptions
        {
            ExcludedDirNames = new HashSet<string>(ctx.Settings.ExcludedFolderNames, StringComparer.OrdinalIgnoreCase),
            SkipCloudOnlyFiles = ctx.Settings.SkipCloudOnlyFiles,
        };
        var stats = FileCopier.CopyDirectory(source, ctx.GetItemDir(item), options, ctx, ct);
        ctx.Info($"{item.DisplayName}: {stats}");
        return Task.CompletedTask;
    }

    public override Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var target = PathTokens.Expand(item.Get("path")!)
                     ?? throw new DirectoryNotFoundException($"Não sei onde fica {item.Get("path")} nesta máquina.");
        var stats = FileCopier.CopyDirectory(ctx.GetItemDir(item), target, CopyOptions.Everything, ctx, ct);
        ctx.Info($"{item.DisplayName} restaurado em {target}: {stats}");
        return Task.CompletedTask;
    }
}
