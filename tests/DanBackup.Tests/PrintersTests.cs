using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Modules;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;
using Xunit.Abstractions;

namespace DanBackup.Tests;

public sealed class PrintersTests(ITestOutputHelper output)
{
    [Fact]
    public void Virtual_printers_are_ignored_and_real_ones_kept()
    {
        const string json = """
        [
          {"Name":"Microsoft Print to PDF","DriverName":"Microsoft Print To PDF","PortName":"PORTPROMPT:","Type":"Local","Shared":false,"ShareName":null,"PortHost":null,"PortNumber":null,"IsDefault":false},
          {"Name":"OneNote (Desktop)","DriverName":"Send to Microsoft OneNote 16 Driver","PortName":"nul:","Type":"Local","Shared":false,"ShareName":null,"PortHost":null,"PortNumber":null,"IsDefault":false},
          {"Name":"Fax","DriverName":"Microsoft Shared Fax Driver","PortName":"SHRFAX:","Type":"Local","Shared":false,"ShareName":null,"PortHost":null,"PortNumber":null,"IsDefault":false},
          {"Name":"Brother Escritório","DriverName":"Brother MFC-L8900CDW series","PortName":"IP_192.168.0.50","Type":"Local","Shared":true,"ShareName":"brother","PortHost":"192.168.0.50","PortNumber":9100,"IsDefault":true},
          {"Name":"\\\\servidor\\hp","DriverName":"HP Universal Printing PCL 6","PortName":"\\\\servidor\\hp","Type":"Connection","Shared":false,"ShareName":null,"PortHost":null,"PortNumber":null,"IsDefault":false}
        ]
        """;
        var printers = PrintersModule.ParsePrinters(json);
        Assert.Equal(["Brother Escritório", @"\\servidor\hp"], printers.Select(p => p.Name));
        Assert.True(printers[1].IsConnection);
        Assert.False(printers[0].HasPlugAndPlayPort);
    }

    [Fact]
    public async Task Discovering_printers_on_this_machine_works_without_admin()
    {
        var items = await new PrintersModule().DiscoverAsync(new AppSettings(), default);
        foreach (var i in items) output.WriteLine($"{i.DisplayName} | {i.Description}");
    }

    [Fact]
    public void Restore_script_quotes_names_with_apostrophes()
    {
        var script = PrintersModule.BuildRestoreScript(new PrintersModule.PrinterInfo(
            "Impressora d'Ana", "Driver X", "IP_10.0.0.1", "Local", false, null, "10.0.0.1", 9100, false));
        Assert.Contains("'Impressora d''Ana'", script);
    }

    /// <summary>
    /// Ponta a ponta real: cria uma impressora de teste (porta TCP/IP fictícia), faz backup, remove, restaura e limpa.
    /// Só roda como administrador com DANBACKUP_PRINTER_TEST=1 e o nome de um driver já instalado em DANBACKUP_PRINTER_DRIVER.
    /// </summary>
    [Fact]
    public async Task Printer_backup_and_restore_end_to_end()
    {
        var driver = Environment.GetEnvironmentVariable("DANBACKUP_PRINTER_DRIVER");
        if (Environment.GetEnvironmentVariable("DANBACKUP_PRINTER_TEST") != "1" || driver is null || !SystemInfo.IsAdministrator())
        {
            output.WriteLine("Ignorado: requer administrador, DANBACKUP_PRINTER_TEST=1 e DANBACKUP_PRINTER_DRIVER.");
            return;
        }

        const string name = "DanBackup Teste";
        const string port = "IP_192.0.2.123";
        var q = PowerShellRunner.Quote;
        var cleanup = $"Remove-Printer -Name {q(name)} -ErrorAction SilentlyContinue; Remove-PrinterPort -Name {q(port)} -ErrorAction SilentlyContinue";
        await PowerShellRunner.RunAsync(cleanup, default);
        var setup = await PowerShellRunner.RunAsync(
            $"$ErrorActionPreference='Stop'; Add-PrinterPort -Name {q(port)} -PrinterHostAddress '192.0.2.123'; Add-Printer -Name {q(name)} -DriverName {q(driver)} -PortName {q(port)}", default);
        Assert.True(setup.ExitCode == 0, setup.CombinedOutput);

        var root = Directory.CreateTempSubdirectory("danbackup-printer-").FullName;
        try
        {
            var module = new PrintersModule();
            var item = Assert.Single(await module.DiscoverAsync(new AppSettings(), default), i => i.DisplayName == name);
            output.WriteLine($"Descoberta: {item.Description}");

            var manifest = await new BackupEngine(new AppSettings(), NullReporter.Instance, "senha-de-teste")
                .RunAsync(root, [new ModulePlan(module, [item])], default);
            Assert.Equal(ItemStatus.Success, manifest.Modules.Single().Items.Single().Status);

            await PowerShellRunner.RunAsync(cleanup, default); // "formatou"

            var restoreItems = manifest.Modules.Single().Items;
            await new RestoreEngine(new AppSettings(), NullReporter.Instance, "senha-de-teste")
                .RunAsync(root, [new ModulePlan(module, restoreItems)], default);
            Assert.True(restoreItems.Single().Status == ItemStatus.Success, restoreItems.Single().Message);

            var after = await PrintersModule.ListPrintersAsync(default);
            var restored = Assert.Single(after, p => p.Name == name);
            Assert.Equal("192.0.2.123", restored.PortHost);
            Assert.Equal(driver, restored.DriverName);
        }
        finally
        {
            await PowerShellRunner.RunAsync(cleanup, default);
            Directory.Delete(root, recursive: true);
        }
    }
}
