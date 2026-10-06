using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Modules;
using DanBackup.Core.Settings;

namespace DanBackup.Tests;

public sealed class CustomFolderTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("danbackup-custom-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Custom_folder_is_copied_recursively_and_restored_to_original_place()
    {
        var source = Path.Combine(_temp, "MeusProjetos");
        Directory.CreateDirectory(Path.Combine(source, "sub", "subsub"));
        Directory.CreateDirectory(Path.Combine(source, "node_modules"));
        File.WriteAllText(Path.Combine(source, "raiz.txt"), "raiz");
        File.WriteAllText(Path.Combine(source, "sub", "subsub", "fundo.bin"), "fundo");
        File.WriteAllText(Path.Combine(source, "node_modules", "lib.js"), "ignorado");

        var settings = new AppSettings { CustomFolders = [source] };
        var module = new UserFoldersModule();
        var item = Assert.Single(await module.DiscoverAsync(settings, default), i => i.Id.StartsWith("custom_"));

        var root = Path.Combine(_temp, "backup");
        var manifest = await new BackupEngine(settings, NullReporter.Instance, "senha-de-teste")
            .RunAsync(root, [new ModulePlan(module, [item])], default);
        Assert.Equal(ItemStatus.Success, manifest.Modules.Single().Items.Single().Status);

        // Fica em modules\user-folders\<id>, com a estrutura original.
        var stored = Path.Combine(root, "modules", "user-folders", item.Id);
        Assert.True(File.Exists(Path.Combine(stored, "raiz.txt")));
        Assert.True(File.Exists(Path.Combine(stored, "sub", "subsub", "fundo.bin")));
        Assert.False(Directory.Exists(Path.Combine(stored, "node_modules")));

        // Restaura no lugar original.
        Directory.Delete(source, recursive: true);
        await new RestoreEngine(settings, NullReporter.Instance, "senha-de-teste")
            .RunAsync(root, [new ModulePlan(module, manifest.Modules.Single().Items)], default);
        Assert.Equal("fundo", File.ReadAllText(Path.Combine(source, "sub", "subsub", "fundo.bin")));
    }
}
