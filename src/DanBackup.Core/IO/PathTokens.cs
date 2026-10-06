using DanBackup.Core.Platform;

namespace DanBackup.Core.IO;

/// <summary>
/// Converte caminhos absolutos em caminhos com tokens (%DOCUMENTS%, %APPDATA%...) e vice-versa,
/// para que um backup feito no usuário "joao" possa ser restaurado no usuário "joao.silva".
/// </summary>
public static class PathTokens
{
    public const string SteamAppPrefix = "STEAMAPP:";

    private static readonly (string Name, Func<string?> Resolve)[] Tokens =
    [
        ("USERPROFILE", () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        ("APPDATA", () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
        ("LOCALAPPDATA", () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        ("LOCALLOW", () => KnownFolders.GetPath(KnownFolders.LocalAppDataLow)),
        ("DOCUMENTS", () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        ("DESKTOP", () => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
        ("DOWNLOADS", () => KnownFolders.GetPath(KnownFolders.Downloads)),
        ("PICTURES", () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
        ("MUSIC", () => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
        ("VIDEOS", () => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        ("FAVORITES", () => Environment.GetFolderPath(Environment.SpecialFolder.Favorites)),
        ("SAVEDGAMES", () => KnownFolders.GetPath(KnownFolders.SavedGames)),
        ("PROGRAMDATA", () => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
        ("PUBLIC", () => Environment.GetEnvironmentVariable("PUBLIC")),
        ("PROGRAMFILES", () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
        ("PROGRAMFILESX86", () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)),
        ("WINDIR", () => Environment.GetFolderPath(Environment.SpecialFolder.Windows)),
        ("STEAM", () => SteamLocator.SteamPath),
    ];

    /// <summary>Resolve um token (sem os %). Retorna null se não existir nesta máquina.</summary>
    public static string? Resolve(string token)
    {
        if (token.StartsWith(SteamAppPrefix, StringComparison.OrdinalIgnoreCase))
            return SteamLocator.GetAppInstallDir(token[SteamAppPrefix.Length..]);

        foreach (var (name, resolve) in Tokens)
        {
            if (name.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                var value = resolve();
                return string.IsNullOrEmpty(value) ? null : value.TrimEnd('\\');
            }
        }

        var env = Environment.GetEnvironmentVariable(token);
        return string.IsNullOrEmpty(env) ? null : env.TrimEnd('\\');
    }

    /// <summary>Expande %TOKENS%. Retorna null se algum token não puder ser resolvido.</summary>
    public static string? Expand(string pattern)
    {
        var result = pattern;
        int start;
        while ((start = result.IndexOf('%')) >= 0)
        {
            int end = result.IndexOf('%', start + 1);
            if (end < 0) break;
            var token = result[(start + 1)..end];
            var value = Resolve(token);
            if (value is null) return null;
            result = result[..start] + value + result[(end + 1)..];
        }
        return result;
    }

    /// <summary>Token inicial de um padrão, ex.: "%DOCUMENTS%\x" → "DOCUMENTS".</summary>
    public static string? LeadingToken(string pattern)
    {
        if (!pattern.StartsWith('%')) return null;
        int end = pattern.IndexOf('%', 1);
        return end > 1 ? pattern[1..end] : null;
    }

    /// <summary>
    /// Encontra o token cujo valor é o maior prefixo do caminho.
    /// <paramref name="hints"/> permite incluir tokens dinâmicos (ex.: STEAMAPP:730).
    /// </summary>
    public static (string Token, string Remainder)? FindToken(string absolutePath, IEnumerable<string>? hints = null)
    {
        var path = Path.GetFullPath(absolutePath).TrimEnd('\\');
        var candidates = Tokens.Select(t => t.Name).Concat(hints ?? []);

        (string Token, string Remainder)? best = null;
        int bestLength = -1;
        foreach (var token in candidates)
        {
            var value = Resolve(token);
            if (value is null || value.Length <= bestLength) continue;
            if (PathUtil.IsSameOrUnder(path, value))
            {
                best = (token, path.Length == value.Length ? "" : path[(value.Length + 1)..]);
                bestLength = value.Length;
            }
        }
        return best;
    }

    /// <summary>Caminho absoluto → caminho com token (ou o próprio caminho se nenhum token servir).</summary>
    public static string Tokenize(string absolutePath, IEnumerable<string>? hints = null)
    {
        var found = FindToken(absolutePath, hints);
        if (found is null) return absolutePath;
        return found.Value.Remainder.Length == 0 ? $"%{found.Value.Token}%" : $"%{found.Value.Token}%\\{found.Value.Remainder}";
    }
}
