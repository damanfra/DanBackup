namespace DanBackup.Core.IO;

/// <summary>
/// Converte caminhos absolutos em caminhos relativos "espelhados" dentro do backup e vice-versa:
/// <c>C:\Users\dan\AppData\Local\VALORANT\x.ini</c> → <c>LOCALAPPDATA\VALORANT\x.ini</c>,
/// <c>D:\Jogos\x.cfg</c> → <c>_DRIVE_D\Jogos\x.cfg</c>.
/// </summary>
public static class MirrorPath
{
    private const string DrivePrefix = "_DRIVE_";
    private const string UncPrefix = "_UNC";

    public static string ToRelative(string absolutePath, IEnumerable<string>? hints = null)
    {
        var found = PathTokens.FindToken(absolutePath, hints);
        if (found is { } f)
        {
            var head = f.Token.Replace(':', '~');
            return f.Remainder.Length == 0 ? head : Path.Combine(head, f.Remainder);
        }

        var full = Path.GetFullPath(absolutePath).TrimEnd('\\');
        if (full.StartsWith(@"\\"))
            return Path.Combine(UncPrefix, full[2..]);
        if (full.Length >= 2 && full[1] == ':')
        {
            var rest = full.Length > 3 ? full[3..] : "";
            var head = DrivePrefix + char.ToUpperInvariant(full[0]);
            return rest.Length == 0 ? head : Path.Combine(head, rest);
        }
        throw new ArgumentException($"Caminho não suportado: {absolutePath}");
    }

    /// <summary>Converte o primeiro segmento de volta para um caminho desta máquina. Null se não resolvível.</summary>
    public static string? ToAbsolute(string relativePath)
    {
        var parts = relativePath.Split('\\', 2);
        var head = parts[0];
        var rest = parts.Length > 1 ? parts[1] : "";

        string? root;
        if (head.StartsWith(DrivePrefix, StringComparison.OrdinalIgnoreCase) && head.Length == DrivePrefix.Length + 1)
            root = head[^1] + @":\";
        else if (head.Equals(UncPrefix, StringComparison.OrdinalIgnoreCase))
            root = @"\\";
        else
            root = PathTokens.Resolve(head.Replace('~', ':'));

        if (root is null) return null;
        return rest.Length == 0 ? root : Path.Combine(root, rest);
    }
}
