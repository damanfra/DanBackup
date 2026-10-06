using System.Text;
using System.Text.Json.Nodes;
using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

/// <summary>Impressoras, portas e drivers de impressão (PrintBrm).</summary>
public sealed class PrintersModule : BackupModuleBase
{
    private const string ItemId = "printers";

    private static string PrintBrm =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\tools\PrintBrm.exe");

    public override string Id => "printers";
    public override string Name => "Impressoras";
    public override string Description => "Impressoras instaladas, portas e drivers (via PrintBrm do Windows).";
    public override bool BackupRequiresAdmin => true;

    public override async Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        if (!File.Exists(PrintBrm)) return [];
        var names = await ListPrinterNamesAsync(ct);
        return
        [
            new BackupItem
            {
                Id = ItemId,
                DisplayName = $"Impressoras ({names.Count})",
                Description = string.Join(", ", names),
            },
        ];
    }

    public override async Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var dir = ctx.GetItemDir(item);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "impressoras.txt"), string.Join(Environment.NewLine, await ListPrinterNamesAsync(ct)), ct);

        ctx.Status("Exportando impressoras (pode demorar)...");
        var file = Path.Combine(dir, "impressoras.printerExport");
        var result = await ProcessRunner.RunAsync(PrintBrm, $"-b -f \"{file}\" -O FORCE", ct);
        if (result.ExitCode != 0 || !File.Exists(file))
            throw new InvalidOperationException($"PrintBrm falhou: {result.LastLine}");
    }

    public override async Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var file = Path.Combine(ctx.GetItemDir(item), "impressoras.printerExport");
        ctx.Status("Importando impressoras (pode demorar)...");
        var result = await ProcessRunner.RunAsync(PrintBrm, $"-r -f \"{file}\" -O FORCE", ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"PrintBrm falhou: {result.LastLine}");
    }

    public override Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var file = new FileInfo(Path.Combine(ctx.GetItemDir(item), "impressoras.printerExport"));
        if (!file.Exists || file.Length == 0)
            throw new InvalidDataException("Exportação de impressoras ausente ou vazia.");
        return Task.CompletedTask;
    }

    private static async Task<List<string>> ListPrinterNamesAsync(CancellationToken ct)
    {
        const string script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; @(Get-Printer | Select-Object -ExpandProperty Name) | ConvertTo-Json";
        try
        {
            var result = await ProcessRunner.RunAsync("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"",
                ct, Encoding.UTF8, TimeSpan.FromMinutes(1));
            var node = JsonNode.Parse(result.Output);
            return node switch
            {
                JsonArray array => array.Select(n => n?.GetValue<string>()).OfType<string>().ToList(),
                JsonValue value => [value.GetValue<string>()],
                _ => [],
            };
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or TimeoutException)
        {
            return [];
        }
    }
}
