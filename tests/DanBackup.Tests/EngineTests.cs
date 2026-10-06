using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Modules;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;
using Xunit.Abstractions;

namespace DanBackup.Tests;

public sealed class EngineTests(ITestOutputHelper output)
{
    private sealed class OutputReporter(ITestOutputHelper output) : IOperationReporter
    {
        public void Log(LogLevel level, string message) => output.WriteLine($"{level}: {message}");
        public void Status(string text) { }
        public void Progress(int done, int total) { }
    }

    /// <summary>Backup real (somente leitura na origem) de módulos leves desta máquina para uma pasta temporária.</summary>
    [Fact]
    public async Task Backup_of_light_modules_writes_manifest_and_files()
    {
        var root = Directory.CreateTempSubdirectory("danbackup-engine-").FullName;
        try
        {
            var settings = new AppSettings();
            var modules = new IBackupModule[] { new AppConfigsModule(), new EnvironmentModule(), new ProgramsModule(), new BrowsersModule() };
            var plan = new List<ModulePlan>();
            foreach (var m in modules)
            {
                var items = await m.DiscoverAsync(settings, default);
                // Navegadores: só o primeiro perfil, para o teste ser rápido.
                plan.Add(new ModulePlan(m, m is BrowsersModule ? items.Take(1).ToList() : items.ToList()));
            }

            var manifest = await new BackupEngine(settings, new OutputReporter(output), "senha-de-teste").RunAsync(root, plan, default);

            Assert.True(BackupManifest.Exists(root));
            var reloaded = BackupManifest.Load(root);
            Assert.Equal(manifest.Modules.Count, reloaded.Modules.Count);
            foreach (var item in reloaded.Modules.SelectMany(m => m.Items))
            {
                output.WriteLine($"{item.Status,-8} {item.DisplayName} {item.Message}");
                Assert.NotEqual(ItemStatus.Failed, item.Status);
            }
            Assert.True(File.Exists(Path.Combine(root, "modules", "programs", "installed-list", "programas.csv")));
            Assert.True(File.Exists(Path.Combine(root, "backup.log")));

            // Itens sensíveis gravados só criptografados; a senha é validada pelo manifesto.
            Assert.NotNull(reloaded.Encryption);
            Assert.True(File.Exists(Path.Combine(root, "modules", "environment", "user", "variaveis.json.dbenc")));
            Assert.False(File.Exists(Path.Combine(root, "modules", "environment", "user", "variaveis.json")));
            Assert.DoesNotContain("senha-de-teste", File.ReadAllText(Path.Combine(root, BackupManifest.FileName)));
            Assert.Throws<InvalidPasswordException>(() => RestoreEngine.OpenCrypto(reloaded, "senha-errada"));
            Assert.NotNull(RestoreEngine.OpenCrypto(reloaded, "senha-de-teste"));

            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f)).ToList();
            output.WriteLine($"--- {files.Count} arquivos ---");
            foreach (var f in files.Take(40)) output.WriteLine(f);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
