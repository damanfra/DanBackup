using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DanBackup.Core.Platform;

public sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public string CombinedOutput => string.IsNullOrWhiteSpace(Error) ? Output : $"{Output}\n{Error}";

    /// <summary>Última linha não vazia da saída — útil para mensagens de erro curtas.</summary>
    public string LastLine => CombinedOutput
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .LastOrDefault() ?? $"código {ExitCode}";
}

public static class ProcessRunner
{
    static ProcessRunner()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Codificação usada pelas ferramentas de console do Windows (netsh, pnputil, reg...).</summary>
    public static Encoding OemEncoding =>
        Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken ct,
        Encoding? encoding = null,
        TimeSpan? timeout = null)
    {
        encoding ??= OemEncoding;
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
        };

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"Não foi possível executar '{fileName}': {ex.Message}", ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromMinutes(30));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* já terminou */ }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException($"'{fileName}' demorou demais e foi interrompido.");
        }

        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    /// <summary>Verifica se um executável existe no PATH.</summary>
    public static bool ExistsOnPath(string exe)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        return paths.Any(p => File.Exists(Path.Combine(p.Trim(), exe)));
    }

    public static bool IsRunning(string processName) => Process.GetProcessesByName(processName).Length > 0;
}
