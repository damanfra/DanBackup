using System.Text;

namespace DanBackup.Core.IO;

public static class PathUtil
{
    private static readonly HashSet<char> Invalid = [.. Path.GetInvalidFileNameChars()];

    /// <summary>Transforma qualquer texto em um nome de arquivo/pasta válido.</summary>
    public static string SafeName(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
            sb.Append(Invalid.Contains(c) ? '_' : c);
        var result = sb.ToString().TrimEnd('.', ' ');
        if (result.Length > 120) result = result[..120];
        return result.Length == 0 ? "_" : result;
    }

    public static bool IsSameOrUnder(string path, string root)
    {
        path = path.TrimEnd('\\');
        root = root.TrimEnd('\\');
        return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
