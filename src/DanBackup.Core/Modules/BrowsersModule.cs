using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

/// <summary>Perfis de navegadores: favoritos, histórico, preferências, extensões (lista) e, no Firefox, senhas.</summary>
public sealed class BrowsersModule : BackupModuleBase
{
    private sealed record Browser(string Id, string Name, string UserDataPattern, string ProcessName, bool SingleProfile = false, bool IsFirefox = false);

    private static readonly Browser[] Browsers =
    [
        new("chrome", "Google Chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data", "chrome"),
        new("edge", "Microsoft Edge", @"%LOCALAPPDATA%\Microsoft\Edge\User Data", "msedge"),
        new("brave", "Brave", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data", "brave"),
        new("vivaldi", "Vivaldi", @"%LOCALAPPDATA%\Vivaldi\User Data", "vivaldi"),
        new("opera", "Opera", @"%APPDATA%\Opera Software\Opera Stable", "opera", SingleProfile: true),
        new("operagx", "Opera GX", @"%APPDATA%\Opera Software\Opera GX Stable", "opera", SingleProfile: true),
        new("firefox", "Mozilla Firefox", @"%APPDATA%\Mozilla\Firefox\Profiles", "firefox", IsFirefox: true),
    ];

    private static readonly string[] ChromiumFiles =
    [
        "Bookmarks", "Bookmarks.bak", "Preferences", "Favicons", "History", "Top Sites", "Shortcuts", "Web Data",
        "Custom Dictionary.txt",
    ];

    private static readonly string[] FirefoxFiles =
    [
        "places.sqlite", "favicons.sqlite", "key4.db", "logins.json", "prefs.js", "user.js", "search.json.mozlz4",
        "handlers.json", "permissions.sqlite", "formhistory.sqlite", "xulstore.json", "containers.json",
        "extensions.json", "extension-settings.json", "extension-preferences.json", "addonStartup.json.lz4",
        "persdict.dat", "cert9.db",
    ];

    private static readonly string[] FirefoxDirs = ["extensions", "browser-extension-data", "chrome"];

    // Senhas (Firefox), certificados e dados de preenchimento automático: salvos criptografados.
    private static readonly HashSet<string> SensitiveFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "logins.json", "key4.db", "cert9.db", "formhistory.sqlite", "Web Data",
    };

    // Arquivos auxiliares gerados no backup, que não voltam para o perfil.
    private static readonly HashSet<string> GeneratedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "favoritos.html", "extensoes.txt", "LEIA-ME.txt",
    };

    public override string Id => "browsers";
    public override string Name => "Navegadores";
    public override string Description => "Chrome, Edge, Brave, Vivaldi, Opera e Firefox: favoritos, histórico, preferências e lista de extensões.";

    public override string? Notes =>
        "Feche os navegadores antes. Senhas do Chrome/Edge/Brave não podem ser copiadas (são criptografadas para esta instalação do Windows): " +
        "use a sincronização da conta ou exporte em Configurações → Senhas → Exportar. No Firefox, as senhas são copiadas. " +
        "🔒 Senhas do Firefox e dados de preenchimento automático são salvos criptografados.";

    public override Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        var items = new List<BackupItem>();
        foreach (var browser in Browsers)
        {
            var root = PathTokens.Expand(browser.UserDataPattern);
            if (root is null || !Directory.Exists(root)) continue;

            foreach (var (dirName, profileName) in FindProfiles(browser, root))
            {
                items.Add(new BackupItem
                {
                    Id = $"{browser.Id}_{PathUtil.SafeName(dirName)}",
                    DisplayName = $"{browser.Name} — {profileName}",
                    Description = Path.Combine(root, dirName),
                    Data = { ["browser"] = browser.Id, ["profileDir"] = dirName },
                });
            }
        }
        return Task.FromResult<IReadOnlyList<BackupItem>>(items);
    }

    private static IEnumerable<(string Dir, string Name)> FindProfiles(Browser browser, string root)
    {
        if (browser.SingleProfile)
        {
            if (File.Exists(Path.Combine(root, "Preferences"))) yield return ("", "Perfil");
            yield break;
        }

        if (browser.IsFirefox)
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                if (!File.Exists(Path.Combine(dir, "prefs.js"))) continue;
                var name = Path.GetFileName(dir);
                var dot = name.IndexOf('.');
                yield return (name, dot >= 0 ? name[(dot + 1)..] : name);
            }
            yield break;
        }

        JsonNode? infoCache = null;
        try
        {
            var localState = Path.Combine(root, "Local State");
            if (File.Exists(localState))
                infoCache = JsonNode.Parse(ReadShared(localState))?["profile"]?["info_cache"];
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // sem nomes amigáveis; usa o nome da pasta
        }

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
            if (!File.Exists(Path.Combine(dir, "Preferences"))) continue;
            var friendly = infoCache?[name]?["name"]?.GetValue<string>() ?? name;
            yield return (name, friendly);
        }
    }

    private static Browser GetBrowser(BackupItem item) =>
        Browsers.First(b => b.Id == item.Get("browser"));

    private static string ProfilePath(BackupItem item)
    {
        var browser = GetBrowser(item);
        var root = PathTokens.Expand(browser.UserDataPattern)
                   ?? throw new DirectoryNotFoundException($"{browser.Name} não encontrado.");
        return Path.Combine(root, item.Get("profileDir")!);
    }

    public override Task BeforeBackupAsync(IReadOnlyList<BackupItem> items, OperationContext ctx, CancellationToken ct)
    {
        foreach (var browser in items.Select(GetBrowser).Distinct())
        {
            if (ProcessRunner.IsRunning(browser.ProcessName))
                ctx.Warn($"{browser.Name} está aberto: alguns arquivos podem não ser copiados. Feche-o para um backup completo.");
        }
        return Task.CompletedTask;
    }

    public override async Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var browser = GetBrowser(item);
        var profile = ProfilePath(item);
        var dir = ctx.GetItemDir(item);
        Directory.CreateDirectory(dir);

        var files = browser.IsFirefox ? FirefoxFiles : ChromiumFiles;
        int copied = 0;
        foreach (var file in files)
        {
            var source = Path.Combine(profile, file);
            var mode = SensitiveFiles.Contains(file) ? CryptoMode.Encrypt : CryptoMode.None;
            if (File.Exists(source) && FileCopier.CopyFile(source, Path.Combine(dir, file), ctx, mode)) copied++;
        }

        if (browser.IsFirefox)
        {
            foreach (var sub in FirefoxDirs.Select(d => Path.Combine(profile, d)).Where(Directory.Exists))
                FileCopier.CopyDirectory(sub, Path.Combine(dir, Path.GetFileName(sub)), CopyOptions.Everything, ctx, ct);
        }
        else
        {
            var bookmarks = Path.Combine(profile, "Bookmarks");
            if (File.Exists(bookmarks))
                await File.WriteAllTextAsync(Path.Combine(dir, "favoritos.html"), BookmarksExporter.ToHtml(ReadShared(bookmarks)), ct);
            await File.WriteAllTextAsync(Path.Combine(dir, "extensoes.txt"), ListChromiumExtensions(profile, browser), ct);
            await File.WriteAllTextAsync(Path.Combine(dir, "LEIA-ME.txt"),
                "favoritos.html pode ser importado em qualquer navegador (Favoritos → Importar).\r\n" +
                "extensoes.txt lista as extensões instaladas, com o link da loja.\r\n" +
                "As senhas NÃO estão aqui: use a sincronização da conta ou exporte-as manualmente no navegador.\r\n", ct);
        }

        ctx.Info($"{item.DisplayName}: {copied} arquivo(s) do perfil salvos.");
    }

    public override Task BeforeRestoreAsync(IReadOnlyList<BackupItem> items, OperationContext ctx, CancellationToken ct)
    {
        var running = items.Select(GetBrowser).Distinct().Where(b => ProcessRunner.IsRunning(b.ProcessName)).ToList();
        if (running.Count > 0)
            throw new InvalidOperationException($"Feche antes de restaurar: {string.Join(", ", running.Select(b => b.Name))}.");
        return Task.CompletedTask;
    }

    public override Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var browser = GetBrowser(item);
        var target = browser.IsFirefox ? FindFirefoxTargetProfile(item) : ProfilePath(item);
        Directory.CreateDirectory(target);

        var source = ctx.GetItemDir(item);
        var options = new CopyOptions
        {
            FileFilter = f => !GeneratedFiles.Contains(f.Name),
            SkipCloudOnlyFiles = false,
            Crypto = CryptoMode.Decrypt,
        };
        var stats = FileCopier.CopyDirectory(source, target, options, ctx, ct);
        ctx.Info($"{item.DisplayName} restaurado em {target}: {stats}");
        return Task.CompletedTask;
    }

    /// <summary>Favoritos legíveis e arquivos sensíveis descriptografáveis.</summary>
    public override Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var dir = ctx.GetItemDir(item);
        var bookmarks = Path.Combine(dir, "Bookmarks");
        if (File.Exists(bookmarks))
        {
            try
            {
                var count = JsonNode.Parse(File.ReadAllText(bookmarks))?["roots"]?.AsObject().Count ?? 0;
                if (count == 0) ctx.Warn("Arquivo de favoritos sem conteúdo.");
            }
            catch (System.Text.Json.JsonException)
            {
                throw new InvalidDataException("Arquivo de favoritos corrompido.");
            }
        }

        if (GetBrowser(item).IsFirefox && !File.Exists(Path.Combine(dir, "places.sqlite")))
            ctx.Warn("places.sqlite (favoritos/histórico do Firefox) não está no backup.");
        return Task.CompletedTask;
    }

    /// <summary>Usa o perfil com o mesmo nome de pasta; senão, o perfil padrão do Firefox desta máquina.</summary>
    private static string FindFirefoxTargetProfile(BackupItem item)
    {
        var profilesRoot = PathTokens.Expand(@"%APPDATA%\Mozilla\Firefox\Profiles")!;
        var same = Path.Combine(profilesRoot, item.Get("profileDir")!);
        if (Directory.Exists(same)) return same;

        var ini = PathTokens.Expand(@"%APPDATA%\Mozilla\Firefox\profiles.ini")!;
        if (File.Exists(ini))
        {
            // [Install...] Default=Profiles/xxxx.default-release tem prioridade.
            var lines = File.ReadAllLines(ini);
            var install = lines.SkipWhile(l => !l.StartsWith("[Install", StringComparison.OrdinalIgnoreCase))
                .Skip(1).TakeWhile(l => !l.StartsWith('['))
                .FirstOrDefault(l => l.StartsWith("Default=", StringComparison.OrdinalIgnoreCase));
            if (install is not null)
            {
                var path = install["Default=".Length..].Replace('/', '\\');
                var full = Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(ini)!, path);
                if (Directory.Exists(full)) return full;
            }
        }

        var any = Directory.Exists(profilesRoot)
            ? Directory.EnumerateDirectories(profilesRoot).FirstOrDefault(d => d.EndsWith(".default-release", StringComparison.OrdinalIgnoreCase))
            : null;
        return any ?? throw new DirectoryNotFoundException("Nenhum perfil do Firefox encontrado. Abra o Firefox uma vez e tente de novo.");
    }

    private static string ListChromiumExtensions(string profile, Browser browser)
    {
        var sb = new StringBuilder();
        var extDir = Path.Combine(profile, "Extensions");
        if (!Directory.Exists(extDir)) return "";
        var storeUrl = browser.Id == "edge"
            ? "https://microsoftedge.microsoft.com/addons/detail/"
            : "https://chromewebstore.google.com/detail/";

        foreach (var ext in Directory.EnumerateDirectories(extDir))
        {
            var id = Path.GetFileName(ext);
            var name = id;
            var versionDir = Directory.EnumerateDirectories(ext).OrderDescending().FirstOrDefault();
            if (versionDir is not null)
                name = ReadExtensionName(versionDir) ?? id;
            sb.AppendLine($"{name}\t{storeUrl}{id}");
        }
        return sb.ToString();
    }

    private static string? ReadExtensionName(string versionDir)
    {
        try
        {
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(versionDir, "manifest.json")));
            var name = manifest?["name"]?.GetValue<string>();
            if (name is null || !name.StartsWith("__MSG_")) return name;

            // Nome traduzido: __MSG_appName__ → _locales/<default_locale>/messages.json
            var key = name[6..^2];
            var locale = manifest?["default_locale"]?.GetValue<string>() ?? "en";
            var messages = JsonNode.Parse(File.ReadAllText(Path.Combine(versionDir, "_locales", locale, "messages.json")))?.AsObject();
            var entry = messages?.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
            return entry?["message"]?.GetValue<string>() ?? name;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>Converte o arquivo Bookmarks (JSON) dos navegadores Chromium para o formato HTML padrão de importação.</summary>
public static class BookmarksExporter
{
    public static string ToHtml(string bookmarksJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
        sb.AppendLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
        sb.AppendLine("<TITLE>Bookmarks</TITLE>");
        sb.AppendLine("<H1>Bookmarks</H1>");
        sb.AppendLine("<DL><p>");

        var roots = JsonNode.Parse(bookmarksJson)?["roots"];
        foreach (var rootName in new[] { "bookmark_bar", "other", "synced" })
        {
            var root = roots?[rootName];
            if (root?["children"] is JsonArray children && children.Count > 0)
                WriteNode(sb, root, 1);
        }

        sb.AppendLine("</DL><p>");
        return sb.ToString();
    }

    private static void WriteNode(StringBuilder sb, JsonNode node, int depth)
    {
        var indent = new string(' ', depth * 4);
        var name = WebUtility.HtmlEncode(node["name"]?.GetValue<string>() ?? "");
        if (node["type"]?.GetValue<string>() == "url")
        {
            var url = WebUtility.HtmlEncode(node["url"]?.GetValue<string>() ?? "");
            sb.AppendLine($"{indent}<DT><A HREF=\"{url}\">{name}</A>");
            return;
        }

        sb.AppendLine($"{indent}<DT><H3>{name}</H3>");
        sb.AppendLine($"{indent}<DL><p>");
        foreach (var child in node["children"]?.AsArray() ?? [])
            if (child is not null) WriteNode(sb, child, depth + 1);
        sb.AppendLine($"{indent}</DL><p>");
    }
}
