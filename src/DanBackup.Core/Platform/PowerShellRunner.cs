using System.Text;

namespace DanBackup.Core.Platform;

/// <summary>
/// Executa scripts no Windows PowerShell via -EncodedCommand (sem problemas de aspas/escape),
/// com saída em UTF-8.
/// </summary>
public static class PowerShellRunner
{
    public static Task<ProcessResult> RunAsync(string script, CancellationToken ct, TimeSpan? timeout = null)
    {
        var full = "[Console]::OutputEncoding = [Text.Encoding]::UTF8\n$ProgressPreference = 'SilentlyContinue'\n" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return ProcessRunner.RunAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            ct, Encoding.UTF8, timeout ?? TimeSpan.FromMinutes(5));
    }

    /// <summary>Literal de string do PowerShell entre aspas simples.</summary>
    public static string Quote(string? value) => "'" + (value ?? "").Replace("'", "''") + "'";
}
