using DanBackup.Core.Catalog;
using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Modules;
using DanBackup.Core.Settings;
using Xunit.Abstractions;

namespace DanBackup.Tests;

public sealed class CoreTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("danbackup-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    private OperationContext Context(string moduleDir) => new()
    {
        ModuleDir = moduleDir,
        Settings = new AppSettings(),
        Reporter = NullReporter.Instance,
    };

    [Fact]
    public void Tokenize_and_expand_round_trip()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jogo", "x.ini");
        var tokenized = PathTokens.Tokenize(path);
        Assert.StartsWith("%LOCALAPPDATA%", tokenized);
        Assert.Equal(path, PathTokens.Expand(tokenized), ignoreCase: true);
    }

    [Fact]
    public void Expand_returns_null_for_unknown_token() =>
        Assert.Null(PathTokens.Expand(@"%NAO_EXISTE_123%\x"));

    [Theory]
    [InlineData(@"Z:\Jogos\cfg\a.cfg", @"_DRIVE_Z\Jogos\cfg\a.cfg")]
    [InlineData(@"\\servidor\share\a.txt", @"_UNC\servidor\share\a.txt")]
    public void MirrorPath_handles_paths_without_tokens(string absolute, string relative)
    {
        Assert.Equal(relative, MirrorPath.ToRelative(absolute));
        Assert.Equal(absolute, MirrorPath.ToAbsolute(relative));
    }

    [Fact]
    public void Glob_resolves_wildcards_in_middle_segments()
    {
        Directory.CreateDirectory(Path.Combine(_temp, "userdata", "111", "730", "cfg"));
        Directory.CreateDirectory(Path.Combine(_temp, "userdata", "222", "730", "cfg"));
        Directory.CreateDirectory(Path.Combine(_temp, "userdata", "333", "570"));
        File.WriteAllText(Path.Combine(_temp, "userdata", "111", "730", "cfg", "a.cfg"), "x");

        var dirs = GlobResolver.Resolve(Path.Combine(_temp, "userdata", "*", "730", "cfg")).ToList();
        Assert.Equal(2, dirs.Count);

        var files = GlobResolver.Resolve(Path.Combine(_temp, "userdata", "*", "730", "cfg", "*.cfg")).ToList();
        Assert.Single(files);
    }

    [Fact]
    public async Task PathSet_backup_and_restore_round_trip()
    {
        var source = Path.Combine(_temp, "origem", "Config");
        Directory.CreateDirectory(Path.Combine(source, "Logs"));
        File.WriteAllText(Path.Combine(source, "input.ini"), "[Teclas]\nPular=Espaço");
        File.WriteAllText(Path.Combine(source, "save.bin"), "save");
        File.WriteAllText(Path.Combine(source, "Logs", "log.txt"), "log");

        var itemDir = Path.Combine(_temp, "backup", "item");
        var options = new CopyOptions
        {
            FileFilter = f => f.Extension is ".ini" or ".txt",
            ExcludedDirNames = new HashSet<string>(["Logs"], StringComparer.OrdinalIgnoreCase),
        };
        var stats = await PathSetCopier.BackupAsync([source], [], itemDir, options, Context(itemDir), default);
        Assert.Equal(1, stats.Files); // só input.ini: save.bin filtrado, Logs excluída

        Directory.Delete(Path.Combine(_temp, "origem"), recursive: true);
        await PathSetCopier.RestoreAsync(itemDir, Context(itemDir), default);
        Assert.Equal("[Teclas]\nPular=Espaço", File.ReadAllText(Path.Combine(source, "input.ini")));
        Assert.False(File.Exists(Path.Combine(source, "save.bin")));
    }

    [Fact]
    public void Bookmarks_are_exported_as_html()
    {
        const string json = """
        { "roots": {
            "bookmark_bar": { "type": "folder", "name": "Barra", "children": [
                { "type": "url", "name": "Exemplo <1>", "url": "https://example.com/?a=1&b=2" },
                { "type": "folder", "name": "Pasta", "children": [ { "type": "url", "name": "B", "url": "https://b.com" } ] } ] },
            "other": { "type": "folder", "name": "Outros", "children": [] } } }
        """;
        var html = BookmarksExporter.ToHtml(json);
        Assert.Contains("<A HREF=\"https://example.com/?a=1&amp;b=2\">Exemplo &lt;1&gt;</A>", html);
        Assert.Contains("<H3>Pasta</H3>", html);
        Assert.DoesNotContain("Outros", html);
    }

    [Theory]
    [InlineData("apps.json")]
    [InlineData("games.json")]
    public void Embedded_catalogs_are_valid(string file)
    {
        var entries = CatalogLoader.LoadEmbedded(file);
        Assert.NotEmpty(entries);
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());
        Assert.All(entries, e => Assert.True(e.Paths.Count + e.Registry.Count > 0, e.Id));
    }

    [Fact]
    public void Module_ids_are_unique() =>
        Assert.Equal(ModuleRegistry.CreateAll().Count, ModuleRegistry.CreateAll().Select(m => m.Id).Distinct().Count());

    /// <summary>Roda a descoberta de todos os módulos nesta máquina (só leitura) e mostra o que foi encontrado.</summary>
    [Fact]
    public async Task Discovery_runs_on_this_machine()
    {
        foreach (var module in ModuleRegistry.CreateAll())
        {
            var items = await module.DiscoverAsync(new AppSettings(), default);
            output.WriteLine($"[{module.Name}] {items.Count} item(ns)");
            foreach (var item in items.Take(15))
                output.WriteLine($"   - {item.DisplayName} | {item.Description}");
            Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        }
    }
}
