using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

/// <summary>Drivers de terceiros instalados (pnputil /export-driver).</summary>
public sealed class DriversModule : BackupModuleBase
{
    private const string ItemId = "oem-drivers";

    public override string Id => "drivers";
    public override string Name => "Drivers";
    public override string Description => "Exporta os drivers de terceiros (não-Microsoft) instalados.";
    public override bool BackupRequiresAdmin => true;

    public override string? Notes =>
        "Ao migrar do Windows 10 para o 11, prefira os drivers do Windows Update ou do fabricante; restaure daqui só o que faltar.";

    public override Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        IReadOnlyList<BackupItem> items =
        [
            new BackupItem { Id = ItemId, DisplayName = "Drivers de terceiros (OEM)", Description = "Placa de vídeo, som, rede, impressoras..." },
        ];
        return Task.FromResult(items);
    }

    public override async Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var dir = ctx.GetItemDir(item);
        Directory.CreateDirectory(dir);
        ctx.Status("Exportando drivers (pode demorar alguns minutos)...");
        var result = await ProcessRunner.RunAsync("pnputil.exe", $"/export-driver * \"{dir}\"", ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"pnputil falhou: {result.LastLine}");

        var list = await ProcessRunner.RunAsync("pnputil.exe", "/enum-drivers", ct);
        await File.WriteAllTextAsync(Path.Combine(ctx.ModuleDir, "lista-drivers.txt"), list.Output, ct);
        ctx.Info($"{Directory.GetDirectories(dir).Length} pacotes de driver exportados.");
    }

    /// <summary>Na restauração, cada pacote de driver é um item separado.</summary>
    public override Task<IReadOnlyList<BackupItem>> GetRestoreItemsAsync(ModuleManifest manifest, string moduleDir, CancellationToken ct)
    {
        var dir = Path.Combine(moduleDir, ItemId);
        if (!Directory.Exists(dir)) return Task.FromResult<IReadOnlyList<BackupItem>>([]);

        IReadOnlyList<BackupItem> items = Directory.GetDirectories(dir)
            .Select(d => new BackupItem
            {
                Id = Path.GetFileName(d),
                DisplayName = Path.GetFileName(d),
                Description = DescribeInf(d),
                SelectedByDefault = false,
                Data = { ["dir"] = Path.GetFileName(d) },
            })
            .OrderBy(i => i.Description)
            .ToList();
        return Task.FromResult(items);
    }

    public override async Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var dir = Path.Combine(ctx.ModuleDir, ItemId, item.Get("dir")!);
        var result = await ProcessRunner.RunAsync("pnputil.exe", $"/add-driver \"{Path.Combine(dir, "*.inf")}\" /install", ct);
        switch (result.ExitCode)
        {
            case 0:
                break;
            case 3010:
                ctx.Warn("Driver instalado; é preciso reiniciar o computador.");
                break;
            case 259:
                ctx.Info("Driver adicionado, mas nenhum dispositivo compatível está conectado agora.");
                break;
            default:
                throw new InvalidOperationException($"pnputil: {result.LastLine}");
        }
    }

    public override Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var dir = ctx.GetItemDir(item);
        var packages = Directory.GetDirectories(dir);
        var withoutInf = packages.Count(p => !Directory.EnumerateFiles(p, "*.inf").Any());
        if (packages.Length == 0)
            throw new InvalidDataException("Nenhum pacote de driver no backup.");
        if (withoutInf > 0)
            ctx.Warn($"{withoutInf} pacote(s) de driver sem arquivo .inf (não instaláveis).");
        ctx.Info($"{packages.Length - withoutInf} pacote(s) de driver instaláveis.");
        return Task.CompletedTask;
    }

    private static string DescribeInf(string dir)
    {
        var inf = Directory.EnumerateFiles(dir, "*.inf").FirstOrDefault();
        if (inf is null) return "";
        string? cls = null, version = null;
        try
        {
            foreach (var line in File.ReadLines(inf).Take(80))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Class", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('=') && !trimmed.StartsWith("ClassGuid", StringComparison.OrdinalIgnoreCase))
                    cls ??= trimmed.Split('=', 2)[1].Trim();
                else if (trimmed.StartsWith("DriverVer", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
                    version ??= trimmed.Split('=', 2)[1].Trim();
            }
        }
        catch (IOException)
        {
            return "";
        }
        return string.Join(" — ", new[] { cls, version }.Where(s => !string.IsNullOrEmpty(s)));
    }
}
