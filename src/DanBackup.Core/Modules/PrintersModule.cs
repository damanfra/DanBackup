using System.Text.Json;
using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

/// <summary>
/// Impressoras: cada fila vira um item com nome, driver, porta (IP) e compartilhamento, lidos pelos cmdlets
/// Get-Printer/Get-PrinterPort (não exige administrador). Na restauração a porta TCP/IP e a fila são recriadas,
/// e impressoras de rede (\\servidor\fila) são reconectadas. Os drivers vêm do módulo Drivers.
/// </summary>
/// <remarks>
/// Versões anteriores usavam o PrintBrm.exe, que falha em várias máquinas (ex.: 0x80510001 ao salvar portas)
/// e retorna código 0 mesmo falhando. Backups antigos com impressoras.printerExport ainda são restaurados por ele.
/// </remarks>
public sealed class PrintersModule : BackupModuleBase
{
    private const string InfoFile = "impressora.json";
    private const string LegacyExportFile = "impressoras.printerExport";

    public sealed record PrinterInfo(
        string Name,
        string? DriverName,
        string? PortName,
        string? Type,
        bool Shared,
        string? ShareName,
        string? PortHost,
        int? PortNumber,
        bool IsDefault)
    {
        public bool IsConnection => string.Equals(Type, "Connection", StringComparison.OrdinalIgnoreCase);

        /// <summary>Porta que não dá para recriar (USB, WSD...): o Windows reinstala ao reconectar a impressora.</summary>
        public bool HasPlugAndPlayPort => !IsConnection && string.IsNullOrEmpty(PortHost) &&
            (PortName?.StartsWith("USB", StringComparison.OrdinalIgnoreCase) == true ||
             PortName?.StartsWith("WSD", StringComparison.OrdinalIgnoreCase) == true);
    }

    // Impressoras que já vêm com o Windows/Office e não precisam de backup.
    private static readonly string[] VirtualDrivers =
    [
        "Microsoft Print To PDF", "Microsoft XPS Document Writer", "Microsoft Shared Fax Driver",
        "Send to Microsoft OneNote", "Microsoft Software Printer Driver",
    ];

    private static readonly HashSet<string> VirtualPorts = new(StringComparer.OrdinalIgnoreCase)
    {
        "PORTPROMPT:", "SHRFAX:", "XPSPort:", "nul:", "FILE:",
    };

    private const string ListScript = """
        $ports = @{}
        Get-PrinterPort -ErrorAction SilentlyContinue | ForEach-Object { $ports[$_.Name] = $_ }
        $default = (Get-CimInstance Win32_Printer -Filter 'Default=TRUE' -ErrorAction SilentlyContinue).Name
        $list = @(Get-Printer -ErrorAction Stop | ForEach-Object {
            $port = $ports[$_.PortName]
            [pscustomobject]@{
                Name = $_.Name; DriverName = $_.DriverName; PortName = $_.PortName; Type = [string]$_.Type
                Shared = [bool]$_.Shared; ShareName = $_.ShareName
                PortHost = $port.PrinterHostAddress; PortNumber = $port.PortNumber
                IsDefault = ($_.Name -eq $default)
            }
        })
        ConvertTo-Json -InputObject $list -Depth 3 -Compress
        """;

    public override string Id => "printers";
    public override string Name => "Impressoras";
    public override string Description => "Impressoras instaladas (locais por IP e de rede), com porta, compartilhamento e impressora padrão.";
    public override bool BackupRequiresAdmin => false;
    public override bool RestoreRequiresAdmin => true;

    public override string? Notes =>
        "Os drivers das impressoras são salvos pelo módulo Drivers — restaure-os antes. " +
        "Impressoras USB/WSD são reinstaladas pelo próprio Windows quando conectadas.";

    public override async Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        List<PrinterInfo> printers;
        try
        {
            printers = await ListPrintersAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or JsonException)
        {
            return []; // serviço de spooler parado/ausente: não há impressoras para salvar
        }

        return printers.Select(p => new BackupItem
        {
            Id = "printer_" + PathUtil.SafeName(p.Name),
            DisplayName = p.Name,
            Description = Describe(p),
            Data = { ["name"] = p.Name },
        }).ToList();
    }

    public override async Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var printer = (await ListPrintersAsync(ct)).FirstOrDefault(p => p.Name == item.Get("name"))
                      ?? throw new InvalidOperationException("A impressora não existe mais neste computador.");
        var dir = ctx.GetItemDir(item);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, InfoFile), JsonSerializer.Serialize(printer, BackupManifest.JsonOptions), ct);
        if (printer.HasPlugAndPlayPort)
            ctx.Warn($"Porta {printer.PortName}: será recriada pelo Windows ao conectar a impressora; a restauração aqui pode não funcionar.");
    }

    public override async Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var dir = ctx.GetItemDir(item);
        var legacy = Path.Combine(dir, LegacyExportFile);
        if (File.Exists(legacy))
        {
            await RestoreLegacyAsync(legacy, ctx, ct);
            return;
        }

        var printer = ReadInfo(dir);
        var result = await PowerShellRunner.RunAsync(BuildRestoreScript(printer), ct);
        var output = result.Output.Trim();
        if (result.ExitCode != 0)
            throw new InvalidOperationException(output.Length > 0 ? output : result.LastLine);
        ctx.Info(output.Contains("JA_EXISTE")
            ? $"{printer.Name}: já existe neste computador; nada foi alterado."
            : $"{printer.Name}: impressora restaurada.");
    }

    public override Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var dir = ctx.GetItemDir(item);
        var legacy = new FileInfo(Path.Combine(dir, LegacyExportFile));
        if (legacy.Exists)
        {
            if (legacy.Length == 0) throw new InvalidDataException("Exportação de impressoras vazia.");
            return Task.CompletedTask;
        }

        var printer = ReadInfo(dir);
        if (!printer.IsConnection && string.IsNullOrEmpty(printer.DriverName))
            throw new InvalidDataException("Impressora sem driver registrado no backup.");
        if (printer.HasPlugAndPlayPort)
            ctx.Warn($"Porta {printer.PortName}: o Windows recria ao conectar a impressora.");
        return Task.CompletedTask;
    }

    private static PrinterInfo ReadInfo(string dir) =>
        JsonSerializer.Deserialize<PrinterInfo>(File.ReadAllText(Path.Combine(dir, InfoFile)), BackupManifest.JsonOptions)
        ?? throw new InvalidDataException("Dados da impressora inválidos.");

    public static async Task<List<PrinterInfo>> ListPrintersAsync(CancellationToken ct)
    {
        var result = await PowerShellRunner.RunAsync(ListScript, ct, TimeSpan.FromMinutes(1));
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Não foi possível listar as impressoras: {result.LastLine}");
        return ParsePrinters(result.Output);
    }

    /// <summary>Converte a saída do script e remove as impressoras virtuais.</summary>
    public static List<PrinterInfo> ParsePrinters(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        var printers = JsonSerializer.Deserialize<List<PrinterInfo>>(json, BackupManifest.JsonOptions) ?? [];
        return printers.Where(p => !IsVirtual(p)).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsVirtual(PrinterInfo p) =>
        VirtualDrivers.Any(d => p.DriverName?.StartsWith(d, StringComparison.OrdinalIgnoreCase) == true) ||
        (p.PortName is not null && VirtualPorts.Contains(p.PortName)) ||
        p.PortName?.StartsWith("OneNote", StringComparison.OrdinalIgnoreCase) == true;

    private static string Describe(PrinterInfo p)
    {
        var parts = new List<string>();
        if (p.IsConnection)
        {
            parts.Add("impressora de rede");
        }
        else
        {
            parts.Add(p.DriverName ?? "driver desconhecido");
            parts.Add(string.IsNullOrEmpty(p.PortHost) ? $"porta {p.PortName}" : $"IP {p.PortHost}");
        }
        if (p.Shared) parts.Add($"compartilhada como {p.ShareName}");
        if (p.IsDefault) parts.Add("padrão");
        return string.Join(" · ", parts);
    }

    /// <summary>Script de restauração; não altera nada se a impressora já existir.</summary>
    public static string BuildRestoreScript(PrinterInfo p)
    {
        var q = PowerShellRunner.Quote;
        return $$"""
            $ErrorActionPreference = 'Stop'
            $name = {{q(p.Name)}}; $driver = {{q(p.DriverName)}}; $port = {{q(p.PortName)}}
            $portHost = {{q(p.PortHost)}}; $portNumber = {{p.PortNumber ?? 9100}}
            try {
                if (Get-Printer -Name $name -ErrorAction SilentlyContinue) { 'JA_EXISTE'; exit 0 }
                if ({{(p.IsConnection ? "$true" : "$false")}}) {
                    Add-Printer -ConnectionName $name
                } else {
                    if (-not (Get-PrinterDriver -Name $driver -ErrorAction SilentlyContinue)) {
                        try { Add-PrinterDriver -Name $driver }
                        catch { throw "O driver '$driver' não está instalado. Restaure antes o módulo Drivers ou instale o driver do fabricante." }
                    }
                    if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) {
                        if ($portHost) { Add-PrinterPort -Name $port -PrinterHostAddress $portHost -PortNumber $portNumber }
                        else { throw "A porta '$port' não existe e não pode ser recriada (USB/WSD): conecte a impressora e o Windows a reinstala." }
                    }
                    Add-Printer -Name $name -DriverName $driver -PortName $port
                    if ({{(p.Shared ? "$true" : "$false")}}) { Set-Printer -Name $name -Shared $true -ShareName {{q(p.ShareName)}} }
                }
                if ({{(p.IsDefault ? "$true" : "$false")}}) {
                    Get-CimInstance Win32_Printer | Where-Object { $_.Name -eq $name } | Invoke-CimMethod -MethodName SetDefaultPrinter | Out-Null
                }
                'OK'
            } catch {
                [Console]::Out.WriteLine($_.Exception.Message); exit 1
            }
            """;
    }

    private static async Task RestoreLegacyAsync(string file, OperationContext ctx, CancellationToken ct)
    {
        var printBrm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\tools\PrintBrm.exe");
        ctx.Status("Importando impressoras com o PrintBrm (backup de versão anterior)...");
        var result = await ProcessRunner.RunAsync(printBrm, $"-r -f \"{file}\" -O FORCE", ct);
        // O PrintBrm retorna 0 mesmo quando falha: confere a mensagem de erro na saída.
        if (result.ExitCode != 0 || result.CombinedOutput.Contains("0x8", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"PrintBrm falhou: {result.LastLine}");
    }
}
