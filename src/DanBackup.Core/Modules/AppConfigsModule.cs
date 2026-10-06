using DanBackup.Core.Catalog;
using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

/// <summary>Configurações de programas conhecidos (catálogo apps.json).</summary>
public sealed class AppConfigsModule : CatalogModuleBase
{
    public override string Id => "app-configs";
    public override string Name => "Configurações de programas";
    public override string Description => "Git, SSH, VS Code, Windows Terminal, Notepad++, OBS, PuTTY e outros.";

    public override Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        IReadOnlyList<BackupItem> items = CatalogLoader.LoadEmbedded("apps.json")
            .Select(FromCatalogEntry)
            .OfType<BackupItem>()
            .ToList();
        return Task.FromResult(items);
    }

    public override async Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        await base.BackupItemAsync(item, ctx, ct);

        if (item.Id == "vscode" && ProcessRunner.ExistsOnPath("code.cmd"))
        {
            var result = await ProcessRunner.RunAsync("cmd.exe", "/c code --list-extensions", ct,
                System.Text.Encoding.UTF8, TimeSpan.FromMinutes(2));
            if (result.ExitCode == 0)
            {
                await File.WriteAllTextAsync(Path.Combine(ctx.GetItemDir(item), "extensoes-vscode.txt"), result.Output, ct);
                ctx.Info("Lista de extensões do VS Code salva (extensoes-vscode.txt).");
            }
        }
    }
}
