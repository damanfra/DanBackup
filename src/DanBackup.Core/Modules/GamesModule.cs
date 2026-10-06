using DanBackup.Core.Catalog;
using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

/// <summary>
/// Configurações de jogos (teclas, gráficos, áudio). Combina:
/// 1) catálogo de jogos conhecidos (games.json); 2) jogos adicionados pelo usuário;
/// 3) varredura de Documentos\My Games, Saved Games, jogos Unreal (%LOCALAPPDATA%\*\Saved\Config),
/// jogos Unity (LocalLow + registro) e Steam userdata.
/// Por padrão copia só arquivos de configuração (extensões nas Configurações).
/// </summary>
public sealed class GamesModule : CatalogModuleBase
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Logs", "Log", "Crashes", "CrashReportClient", "CrashDumps", "Cache", "ShaderCache", "Shaders", "webcache",
        "Screenshots", "Movies", "Videos", "Replays", "Demos", "Temp", "GPUCache", "Code Cache",
    };

    private static readonly HashSet<string> IgnoredSteamApps = ["760" /* screenshots */];

    private static readonly Dictionary<string, string> KnownSteamApps = new()
    {
        ["7"] = "Steam (cliente)",
        ["241100"] = "Steam Input (configurações de controle)",
    };

    public override string Id => "games";
    public override string Name => "Configurações de jogos";
    public override string Description => "Teclas, gráficos e outras configurações (.ini, .cfg, .xml...) dos jogos encontrados.";

    public override string? Notes =>
        "Por padrão só arquivos de configuração são copiados, não saves. Isso, as extensões e jogos extras podem ser ajustados nas Configurações.";

    public override Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct) =>
        Task.Run(() => Discover(settings, ct), ct);

    private IReadOnlyList<BackupItem> Discover(AppSettings settings, CancellationToken ct)
    {
        var items = new List<BackupItem>();
        var claimed = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filter = CreateFilter(settings);

        void Add(string id, string name, string source, IReadOnlyList<string> patterns, IReadOnlyList<string> registry, IReadOnlyList<string> resolved)
        {
            var uniqueId = id;
            for (int n = 2; !ids.Add(uniqueId); n++) uniqueId = $"{id}_{n}";
            var where = resolved.Count > 0 ? resolved[0] + (resolved.Count > 1 ? $" (+{resolved.Count - 1})" : "") : registry.FirstOrDefault();
            items.Add(CreateItem(uniqueId, name, $"{source} · {where}", patterns, registry));
            claimed.AddRange(resolved);
        }

        bool IsClaimed(string path) =>
            claimed.Any(c => PathUtil.IsSameOrUnder(path, c) || PathUtil.IsSameOrUnder(c, path));

        // 1) Catálogo
        foreach (var entry in CatalogLoader.LoadEmbedded("games.json"))
        {
            ct.ThrowIfCancellationRequested();
            var patterns = entry.Paths.Where(p => ResolveExisting(p).Any()).ToList();
            var registry = entry.Registry.Where(RegistryHelper.KeyExists).ToList();
            if (patterns.Count == 0 && registry.Count == 0) continue;
            Add("cat_" + entry.Id, entry.Name, "Catálogo", patterns, registry, patterns.SelectMany(ResolveExisting).ToList());
        }

        // 2) Jogos do usuário
        foreach (var game in settings.CustomGames)
        {
            var path = PathTokens.Expand(game.Path) ?? game.Path;
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            Add("custom_" + PathUtil.SafeName(game.Name), game.Name, "Adicionado por você", [PathTokens.Tokenize(path)], [], [path]);
        }

        // 3) Varreduras
        void AddFolder(string source, string dir, string? name = null, IReadOnlyList<string>? registry = null)
        {
            ct.ThrowIfCancellationRequested();
            if (IsClaimed(dir)) return;
            bool hasRegistry = registry is { Count: > 0 };
            bool hasFiles = HasMatchingFiles(dir, filter);
            if (!hasFiles && !hasRegistry) return;
            name ??= Path.GetFileName(dir);
            Add("scan_" + PathUtil.SafeName(name), name, source,
                hasFiles ? [PathTokens.Tokenize(dir)] : [], registry ?? [], hasFiles ? [dir] : []);
        }

        foreach (var dir in Subdirs(PathTokens.Expand(@"%DOCUMENTS%\My Games")))
            AddFolder("My Games", dir);

        foreach (var dir in Subdirs(PathTokens.Resolve("SAVEDGAMES")))
            AddFolder("Saved Games", dir);

        // Unreal Engine: %LOCALAPPDATA%\<Jogo>\Saved\Config
        foreach (var dir in Subdirs(PathTokens.Resolve("LOCALAPPDATA")))
        {
            var config = Path.Combine(dir, "Saved", "Config");
            if (Directory.Exists(config))
                AddFolder("Unreal Engine", config, Path.GetFileName(dir));
        }

        // Unity: LocalLow\<Empresa>\<Jogo> (com Player.log) + HKCU\Software\<Empresa>\<Jogo> (PlayerPrefs)
        foreach (var company in Subdirs(PathTokens.Resolve("LOCALLOW")))
        {
            foreach (var product in Subdirs(company))
            {
                var regKey = $@"HKCU\Software\{Path.GetFileName(company)}\{Path.GetFileName(product)}";
                bool isUnity = File.Exists(Path.Combine(product, "Player.log")) || File.Exists(Path.Combine(product, "Player-prev.log"));
                if (!isUnity) continue;
                AddFolder("Unity", product, Path.GetFileName(product), RegistryHelper.KeyExists(regKey) ? [regKey] : []);
            }
        }

        AddSteam(Add, IsClaimed, filter, ct);

        return items.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private delegate void AddItem(string id, string name, string source, IReadOnlyList<string> patterns,
        IReadOnlyList<string> registry, IReadOnlyList<string> resolved);

    private static void AddSteam(AddItem add, Func<string, bool> isClaimed,
        Func<FileInfo, bool> filter, CancellationToken ct)
    {
        var steam = SteamLocator.SteamPath;
        if (steam is null) return;
        var userdata = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userdata)) return;

        // Opções de inicialização por jogo e atalhos de jogos não-Steam.
        var clientConfig = new[] { @"%STEAM%\userdata\*\config\localconfig.vdf", @"%STEAM%\userdata\*\config\shortcuts.vdf" };
        var clientResolved = clientConfig.SelectMany(ResolveExisting).ToList();
        if (clientResolved.Count > 0)
            add("steam_client", "Steam — opções de inicialização e atalhos", "Steam", clientConfig, [], []);

        var appDirs = Subdirs(userdata)
            .SelectMany(Subdirs)
            .Where(d => Path.GetFileName(d).All(char.IsDigit))
            .GroupBy(Path.GetFileName);

        foreach (var group in appDirs)
        {
            ct.ThrowIfCancellationRequested();
            var appId = group.Key!;
            if (IgnoredSteamApps.Contains(appId)) continue;
            var dirs = group.Where(d => !isClaimed(d) && HasMatchingFiles(d, filter)).ToList();
            if (dirs.Count == 0) continue;

            var name = KnownSteamApps.GetValueOrDefault(appId)
                       ?? (SteamLocator.Apps.TryGetValue(appId, out var app) ? app.Name : $"Steam App {appId} (não instalado)");
            add("steam_" + appId, name, "Steam userdata", [$@"%STEAM%\userdata\*\{appId}"], [], dirs);
        }
    }

    protected override CopyOptions GetDirectoryOptions(OperationContext ctx) => new()
    {
        FileFilter = CreateFilter(ctx.Settings),
        ExcludedDirNames = ExcludedDirs,
        SkipCloudOnlyFiles = ctx.Settings.SkipCloudOnlyFiles,
    };

    private static Func<FileInfo, bool> CreateFilter(AppSettings settings)
    {
        if (settings.GameIncludeAllFiles) return _ => true;
        var extensions = new HashSet<string>(
            settings.GameConfigExtensions.Select(e => e.StartsWith('.') ? e : "." + e),
            StringComparer.OrdinalIgnoreCase);
        long maxBytes = Math.Max(1, settings.GameMaxFileSizeMB) * 1024L * 1024L;
        return f => extensions.Contains(f.Extension) && f.Length <= maxBytes;
    }

    private static IEnumerable<string> Subdirs(string? dir)
    {
        if (dir is null || !Directory.Exists(dir)) return [];
        try
        {
            return Directory.EnumerateDirectories(dir, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Verifica (rapidamente) se a pasta tem algum arquivo que passaria no filtro.</summary>
    private static bool HasMatchingFiles(string dir, Func<FileInfo, bool> filter)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MaxRecursionDepth = 6,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*", options)
                .Take(5000)
                .Any(f => !ExcludedDirs.Contains(f.Directory?.Name ?? "") && filter(f));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
