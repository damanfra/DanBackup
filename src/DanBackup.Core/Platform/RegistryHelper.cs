using Microsoft.Win32;

namespace DanBackup.Core.Platform;

public static class RegistryHelper
{
    public static (RegistryKey Hive, string SubKey)? Parse(string fullKey)
    {
        var parts = fullKey.Trim().TrimEnd('\\').Split('\\', 2);
        RegistryKey? hive = parts[0].ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
            _ => null,
        };
        if (hive is null) return null;
        return (hive, parts.Length > 1 ? parts[1] : "");
    }

    public static bool KeyExists(string fullKey)
    {
        var parsed = Parse(fullKey);
        if (parsed is null) return false;
        try
        {
            using var key = parsed.Value.Hive.OpenSubKey(parsed.Value.SubKey);
            return key is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true; // existe, mas sem permissão de leitura agora
        }
    }

    public static bool IsMachineWide(string fullKey) =>
        fullKey.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ||
        fullKey.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);

    public static async Task ExportAsync(string fullKey, string file, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var result = await ProcessRunner.RunAsync("reg.exe", $"export \"{fullKey}\" \"{file}\" /y", ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"reg export falhou para {fullKey}: {result.LastLine}");
    }

    public static async Task ImportAsync(string file, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("reg.exe", $"import \"{file}\"", ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"reg import falhou para {Path.GetFileName(file)}: {result.LastLine}");
    }
}
