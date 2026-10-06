using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;
using Microsoft.Win32;

namespace DanBackup.Core.Modules;

/// <summary>
/// Lista de programas instalados (para consulta) e exportação do winget.
/// A reinstalação acontece só para os pacotes que o usuário escolher na tela de restauração.
/// </summary>
public sealed class ProgramsModule : BackupModuleBase
{
    private const string ListItemId = "installed-list";
    private const string WingetItemId = "winget";
    private const string WingetFile = "winget-export.json";

    public sealed record InstalledProgram(string Name, string? Version, string? Publisher, string? InstallDate, string? InstallLocation);

    public override string Id => "programs";
    public override string Name => "Programas instalados";
    public override string Description => "Lista dos programas instalados e exportação do winget para reinstalar depois.";

    public override string? Notes =>
        "Nada é reinstalado automaticamente: na restauração você escolhe pacote por pacote o que o winget deve instalar.";

    public override Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        var items = new List<BackupItem>
        {
            new()
            {
                Id = ListItemId,
                DisplayName = "Lista de programas instalados",
                Description = $"{ReadInstalledPrograms().Count} programas (salvo em CSV/JSON para consulta)",
            },
        };
        if (IsWingetAvailable())
        {
            items.Add(new BackupItem
            {
                Id = WingetItemId,
                DisplayName = "Exportação do winget",
                Description = "Permite reinstalar os programas escolhidos depois, com um clique",
            });
        }
        return Task.FromResult<IReadOnlyList<BackupItem>>(items);
    }

    public override async Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var dir = ctx.GetItemDir(item);
        Directory.CreateDirectory(dir);

        if (item.Id == ListItemId)
        {
            var programs = ReadInstalledPrograms();
            await File.WriteAllTextAsync(Path.Combine(dir, "programas.json"),
                JsonSerializer.Serialize(programs, BackupManifest.JsonOptions), ct);

            var csv = new StringBuilder("Nome;Versão;Fabricante;Instalado em;Local\r\n");
            foreach (var p in programs)
                csv.AppendLine(string.Join(';', new[] { p.Name, p.Version, p.Publisher, p.InstallDate, p.InstallLocation }.Select(Csv)));
            // UTF-8 com BOM para o Excel abrir os acentos corretamente.
            await File.WriteAllTextAsync(Path.Combine(dir, "programas.csv"), csv.ToString(), new UTF8Encoding(true), ct);
            ctx.Info($"{programs.Count} programas listados.");
        }
        else
        {
            var file = Path.Combine(dir, WingetFile);
            var result = await ProcessRunner.RunAsync("winget.exe",
                $"export -o \"{file}\" --accept-source-agreements --disable-interactivity", ct, Encoding.UTF8, TimeSpan.FromMinutes(10));
            if (!File.Exists(file))
                throw new InvalidOperationException($"winget export falhou: {result.LastLine}");
            await File.WriteAllTextAsync(Path.Combine(dir, "winget-export.log"), result.CombinedOutput, ct);
            if (result.ExitCode != 0)
                ctx.Warn("Alguns programas não estão disponíveis no winget (veja winget-export.log).");
        }
    }

    /// <summary>Na restauração, cada pacote do winget vira um item que o usuário marca individualmente.</summary>
    public override async Task<IReadOnlyList<BackupItem>> GetRestoreItemsAsync(ModuleManifest manifest, string moduleDir, CancellationToken ct)
    {
        var file = Path.Combine(moduleDir, WingetItemId, WingetFile);
        if (!File.Exists(file)) return [];

        var root = JsonNode.Parse(await File.ReadAllTextAsync(file, ct));
        var items = new List<BackupItem>();
        foreach (var source in root?["Sources"]?.AsArray() ?? [])
        {
            var sourceName = source?["SourceDetails"]?["Name"]?.GetValue<string>() ?? "winget";
            foreach (var package in source?["Packages"]?.AsArray() ?? [])
            {
                var id = package?["PackageIdentifier"]?.GetValue<string>();
                if (string.IsNullOrEmpty(id)) continue;
                items.Add(new BackupItem
                {
                    Id = $"pkg:{id}",
                    DisplayName = id,
                    Description = $"fonte: {sourceName}",
                    SelectedByDefault = false,
                    Data = { ["package"] = id, ["source"] = sourceName },
                });
            }
        }
        return items.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public override async Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var dir = ctx.GetItemDir(item);
        if (item.Id == ListItemId)
        {
            var programs = JsonSerializer.Deserialize<List<InstalledProgram>>(
                await File.ReadAllTextAsync(Path.Combine(dir, "programas.json"), ct), BackupManifest.JsonOptions);
            ctx.Info($"Lista com {programs?.Count ?? 0} programas.");
            return;
        }

        var manifest = new ModuleManifest { Id = Id, Name = Name, Items = [item] };
        var packages = await GetRestoreItemsAsync(manifest, ctx.ModuleDir, ct);
        if (packages.Count == 0)
            ctx.Warn("A exportação do winget não tem nenhum pacote reinstalável.");
        else
            ctx.Info($"{packages.Count} pacotes do winget disponíveis para reinstalar.");
    }

    public override Task BeforeRestoreAsync(IReadOnlyList<BackupItem> items, OperationContext ctx, CancellationToken ct)
    {
        if (!IsWingetAvailable())
            throw new InvalidOperationException("winget não encontrado. Instale o \"Instalador de Aplicativo\" pela Microsoft Store.");
        return Task.CompletedTask;
    }

    public override async Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var id = item.Get("package")!;
        ctx.Info($"Instalando {id}...");
        var result = await ProcessRunner.RunAsync("winget.exe",
            $"install --id \"{id}\" --exact --source \"{item.Get("source")}\" --accept-package-agreements --accept-source-agreements --disable-interactivity",
            ct, Encoding.UTF8, TimeSpan.FromMinutes(30));
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"winget: {result.LastLine}");
    }

    private static bool IsWingetAvailable() =>
        ProcessRunner.ExistsOnPath("winget.exe") ||
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe"));

    private static string Csv(string? value)
    {
        value ??= "";
        return value.Contains(';') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    public static List<InstalledProgram> ReadInstalledPrograms()
    {
        const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var sources = new (RegistryKey Hive, string Path)[]
        {
            (Registry.LocalMachine, uninstall),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, uninstall),
        };

        var programs = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, path) in sources)
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key?.GetValue("DisplayName") is not string displayName || string.IsNullOrWhiteSpace(displayName)) continue;
                if (key.GetValue("SystemComponent") is int sc && sc == 1) continue;
                if (key.GetValue("ParentKeyName") is not null) continue;
                if (key.GetValue("ReleaseType") is string rt && (rt.Contains("Update") || rt.Contains("Hotfix"))) continue;

                var version = key.GetValue("DisplayVersion") as string;
                programs.TryAdd($"{displayName}|{version}", new InstalledProgram(
                    displayName.Trim(), version, key.GetValue("Publisher") as string,
                    key.GetValue("InstallDate") as string, key.GetValue("InstallLocation") as string));
            }
        }
        return programs.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
