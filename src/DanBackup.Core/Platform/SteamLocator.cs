using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DanBackup.Core.Platform;

public sealed record SteamApp(string AppId, string Name, string InstallDir);

/// <summary>
/// Localiza a instalação do Steam, suas bibliotecas e jogos instalados (lendo os arquivos .vdf/.acf).
/// </summary>
public static partial class SteamLocator
{
    private static readonly Lazy<string?> _steamPath = new(FindSteamPath);
    private static readonly Lazy<IReadOnlyDictionary<string, SteamApp>> _apps = new(LoadApps);

    public static string? SteamPath => _steamPath.Value;

    public static IReadOnlyDictionary<string, SteamApp> Apps => _apps.Value;

    public static string? GetAppInstallDir(string appId) => Apps.TryGetValue(appId, out var app) ? app.InstallDir : null;

    private static string? FindSteamPath()
    {
        string? path = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
        path ??= Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")?.GetValue("InstallPath") as string;
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = Path.GetFullPath(path.Replace('/', '\\'));
        return Directory.Exists(path) ? path : null;
    }

    public static IReadOnlyList<string> GetLibraryFolders()
    {
        var steam = SteamPath;
        if (steam is null) return [];
        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in VdfPathRegex().Matches(File.ReadAllText(vdf)))
            {
                var lib = m.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(lib) && !libraries.Contains(lib, StringComparer.OrdinalIgnoreCase))
                    libraries.Add(lib);
            }
        }
        return libraries;
    }

    private static IReadOnlyDictionary<string, SteamApp> LoadApps()
    {
        var apps = new Dictionary<string, SteamApp>();
        foreach (var lib in GetLibraryFolders())
        {
            var steamapps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                try
                {
                    var text = File.ReadAllText(acf);
                    var id = AcfValue(text, "appid");
                    var name = AcfValue(text, "name");
                    var dir = AcfValue(text, "installdir");
                    if (id is null || name is null || dir is null) continue;
                    apps[id] = new SteamApp(id, name, Path.Combine(steamapps, "common", dir));
                }
                catch (IOException)
                {
                    // manifesto em uso / ilegível: ignora
                }
            }
        }
        return apps;
    }

    private static string? AcfValue(string text, string key)
    {
        var m = Regex.Match(text, $"\"{key}\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPathRegex();
}
