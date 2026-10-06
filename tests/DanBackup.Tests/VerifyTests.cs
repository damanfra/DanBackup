using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Modules;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;
using Xunit.Abstractions;

namespace DanBackup.Tests;

public sealed class VerifyTests(ITestOutputHelper output) : IDisposable
{
    private const string Password = "senha-de-teste";
    private readonly string _root = Directory.CreateTempSubdirectory("danbackup-verify-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class OutputReporter(ITestOutputHelper output) : IOperationReporter
    {
        public void Log(LogLevel level, string message) => output.WriteLine($"{level}: {message}");
        public void Status(string text) { }
        public void Progress(int done, int total) { }
    }

    private async Task CreateBackupAsync()
    {
        var settings = new AppSettings();
        var plan = new List<ModulePlan>();
        foreach (var m in new IBackupModule[] { new AppConfigsModule(), new EnvironmentModule(), new ProgramsModule() })
            plan.Add(new ModulePlan(m, (await m.DiscoverAsync(settings, default)).ToList()));
        await new BackupEngine(settings, NullReporter.Instance, Password).RunAsync(_root, plan, default);
    }

    /// <summary>Monta o plano de verificação como a tela faz: todos os itens do manifesto.</summary>
    private async Task<List<BackupItem>> VerifyAsync(string? password)
    {
        var manifest = BackupManifest.Load(_root);
        var modules = ModuleRegistry.CreateAll().ToDictionary(m => m.Id);
        var plan = manifest.Modules.Select(mm => new ModulePlan(modules[mm.Id], mm.Items.Select(i =>
        {
            var c = i.Clone();
            c.Status = i.Status;
            c.Message = i.Message;
            return c;
        }).ToList())).ToList();

        await new VerifyEngine(new AppSettings(), new OutputReporter(output), password).RunAsync(_root, plan, default);
        return plan.SelectMany(p => p.Items).ToList();
    }

    [Fact]
    public async Task Intact_backup_is_fully_recoverable()
    {
        await CreateBackupAsync();
        Assert.True(File.Exists(Path.Combine(_root, Checksums.FileName)));

        var items = await VerifyAsync(Password);
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.NotEqual(ItemStatus.Failed, i.Status));
    }

    [Fact]
    public async Task Corrupted_missing_and_tampered_files_are_detected()
    {
        await CreateBackupAsync();

        // 1) arquivo comum corrompido
        var csv = Path.Combine(_root, "modules", "programs", "installed-list", "programas.csv");
        File.AppendAllText(csv, "linha adulterada");
        // 2) arquivo criptografado adulterado
        var env = Path.Combine(_root, "modules", "environment", "user", "variaveis.json.dbenc");
        var bytes = File.ReadAllBytes(env);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(env, bytes);
        // 3) arquivo apagado
        File.Delete(Path.Combine(_root, "modules", "environment", "system", "variaveis.json.dbenc"));

        var items = await VerifyAsync(Password);
        Assert.Equal(ItemStatus.Failed, items.Single(i => i.Id == "installed-list").Status);
        Assert.Equal(ItemStatus.Failed, items.Single(i => i.Id == "user").Status);
        Assert.Equal(ItemStatus.Failed, items.Single(i => i.Id == "system").Status);
        Assert.Contains("faltando", items.Single(i => i.Id == "system").Message);
        Assert.NotEqual(ItemStatus.Failed, items.Single(i => i.Id == "winget").Status);
    }

    [Fact]
    public async Task Wrong_password_stops_verification()
    {
        await CreateBackupAsync();
        await Assert.ThrowsAsync<InvalidPasswordException>(() => VerifyAsync("senha-errada"));
    }
}
