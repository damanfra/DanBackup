using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

/// <summary>Arquivos PST do Outlook, assinaturas, autocompletar e modelos do Office.</summary>
public sealed class OfficeModule : CatalogModuleBase
{
    private static readonly string[] PstSearchDirs =
    [
        @"%DOCUMENTS%\Outlook Files",
        @"%DOCUMENTS%\Arquivos do Outlook",
        @"%LOCALAPPDATA%\Microsoft\Outlook",
        @"%APPDATA%\Microsoft\Outlook",
    ];

    public override string Id => "office";
    public override string Name => "Outlook e Office";
    public override string Description => "Arquivos de dados .pst, assinaturas, autocompletar do Outlook e modelos do Word (Normal.dotm).";

    public override string? Notes =>
        "Feche o Outlook antes. Arquivos .ost não são salvos (são cache do servidor). Depois de restaurar um .pst, abra-o no Outlook em Arquivo → Abrir.";

    public override Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        var items = new List<BackupItem>();

        foreach (var dir in PstSearchDirs.Select(PathTokens.Expand).OfType<string>().Where(Directory.Exists))
        {
            foreach (var pst in Directory.EnumerateFiles(dir, "*.pst", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                var size = PathUtil.FormatSize(new FileInfo(pst).Length);
                items.Add(CreateItem($"pst{items.Count}_" + PathUtil.SafeName(Path.GetFileNameWithoutExtension(pst)),
                    $"Outlook: {Path.GetFileName(pst)}", $"{pst} ({size})", [PathTokens.Tokenize(pst)], []));
            }
        }

        AddIfExists(items, "signatures", "Assinaturas do Outlook", [@"%APPDATA%\Microsoft\Signatures"]);
        AddIfExists(items, "autocomplete", "Autocompletar do Outlook (Stream_Autocomplete)", [@"%LOCALAPPDATA%\Microsoft\Outlook\RoamCache\Stream_Autocomplete*.dat"]);
        AddIfExists(items, "templates", "Modelos do Office (Normal.dotm etc.)", [@"%APPDATA%\Microsoft\Templates"]);
        AddIfExists(items, "dictionaries", "Dicionário personalizado do Office", [@"%APPDATA%\Microsoft\UProof"]);

        return Task.FromResult<IReadOnlyList<BackupItem>>(items);
    }

    private static void AddIfExists(List<BackupItem> items, string id, string name, string[] patterns)
    {
        if (patterns.Any(p => ResolveExisting(p).Any()))
            items.Add(CreateItem(id, name, PathTokens.Expand(patterns[0]), patterns, []));
    }

    public override Task BeforeBackupAsync(IReadOnlyList<BackupItem> items, OperationContext ctx, CancellationToken ct)
    {
        if (ProcessRunner.IsRunning("OUTLOOK"))
            ctx.Warn("O Outlook está aberto: arquivos .pst podem ser copiados incompletos. Feche-o e refaça se necessário.");
        return Task.CompletedTask;
    }

    public override Task BeforeRestoreAsync(IReadOnlyList<BackupItem> items, OperationContext ctx, CancellationToken ct)
    {
        if (ProcessRunner.IsRunning("OUTLOOK") || ProcessRunner.IsRunning("WINWORD"))
            throw new InvalidOperationException("Feche o Outlook e o Word antes de restaurar.");
        return Task.CompletedTask;
    }
}
