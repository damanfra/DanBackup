using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace DanBackup.Core.Platform;

public static class SystemInfo
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string GetOsDescription()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = Environment.OSVersion.Version.Build;
        var edition = key?.GetValue("EditionID") as string ?? "";
        var display = key?.GetValue("DisplayVersion") as string ?? "";
        var name = build >= 22000 ? "Windows 11" : "Windows 10";
        return $"{name} {edition} {display} (build {build})".Replace("  ", " ");
    }

    /// <summary>Data de instalação do Windows (redefinida a cada atualização de versão).</summary>
    public static DateTime? GetWindowsInstallDate()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key?.GetValue("InstallDate") is int seconds)
            return DateTimeOffset.FromUnixTimeSeconds((uint)seconds).LocalDateTime;
        return null;
    }
}

public static class KnownFolders
{
    public static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");
    public static readonly Guid SavedGames = new("4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4");
    public static readonly Guid LocalAppDataLow = new("A520A1A4-1780-4FF6-BD18-167343C5AF16");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    public static string? GetPath(Guid id)
    {
        int hr = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var ptr);
        try
        {
            return hr == 0 ? Marshal.PtrToStringUni(ptr) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }
}
