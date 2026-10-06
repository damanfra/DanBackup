namespace DanBackup.Core.IO;

/// <summary>
/// Resolve caminhos com curingas em qualquer segmento, ex.: <c>C:\Steam\userdata\*\730\local\cfg</c>.
/// Retorna somente caminhos que existem (arquivos ou pastas).
/// </summary>
public static class GlobResolver
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    public static IEnumerable<string> Resolve(string pattern)
    {
        if (!HasWildcard(pattern))
        {
            if (File.Exists(pattern) || Directory.Exists(pattern)) yield return pattern;
            yield break;
        }

        var segments = pattern.Split('\\');
        int firstWild = Array.FindIndex(segments, HasWildcard);
        var root = string.Join('\\', segments[..firstWild]);
        if (root.EndsWith(':')) root += '\\';
        if (!Directory.Exists(root)) yield break;

        foreach (var p in Expand(root, segments, firstWild))
            yield return p;
    }

    private static IEnumerable<string> Expand(string current, string[] segments, int index)
    {
        if (index == segments.Length)
        {
            yield return current;
            yield break;
        }

        var segment = segments[index];
        bool last = index == segments.Length - 1;

        if (!HasWildcard(segment))
        {
            var next = Path.Combine(current, segment);
            if (last ? File.Exists(next) || Directory.Exists(next) : Directory.Exists(next))
                foreach (var p in Expand(next, segments, index + 1)) yield return p;
            yield break;
        }

        IEnumerable<string> matches;
        try
        {
            matches = last
                ? Directory.EnumerateFileSystemEntries(current, segment, Options).ToList()
                : Directory.EnumerateDirectories(current, segment, Options).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var match in matches)
            foreach (var p in Expand(match, segments, index + 1))
                yield return p;
    }

    private static bool HasWildcard(string s) => s.Contains('*') || s.Contains('?');
}
