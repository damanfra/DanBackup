using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DanBackup.Core.Engine;

namespace DanBackup.Core.IO;

/// <summary>
/// Arquivo checksums.sha256 na raiz do backup, no formato do sha256sum ("hash *caminho/relativo"),
/// com o hash de cada arquivo em modules/. Permite detectar arquivos faltando ou corrompidos.
/// </summary>
public static class Checksums
{
    public const string FileName = "checksums.sha256";

    public static async Task WriteAsync(string backupRoot, IOperationReporter reporter, CancellationToken ct)
    {
        var modules = Path.Combine(backupRoot, "modules");
        if (!Directory.Exists(modules)) return;

        var files = Directory.EnumerateFiles(modules, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        var sb = new StringBuilder();
        var throttle = Stopwatch.StartNew();
        for (int i = 0; i < files.Count; i++)
        {
            if (throttle.ElapsedMilliseconds > 200)
            {
                reporter.Status($"Calculando checksums ({i + 1}/{files.Count})...");
                throttle.Restart();
            }
            var relative = Path.GetRelativePath(backupRoot, files[i]).Replace('\\', '/');
            sb.Append(await ComputeAsync(files[i], ct)).Append(" *").Append(relative).Append('\n');
        }

        var path = Path.Combine(backupRoot, FileName);
        await File.WriteAllTextAsync(path + ".tmp", sb.ToString(), new UTF8Encoding(false), ct);
        File.Move(path + ".tmp", path, overwrite: true);
        reporter.Log(LogLevel.Info, $"Checksums de {files.Count} arquivo(s) gravados em {FileName}.");
    }

    /// <summary>Caminho relativo à raiz do backup (com '\') → hash. Null se o backup não tiver checksums.</summary>
    public static Dictionary<string, string>? Load(string backupRoot)
    {
        var path = Path.Combine(backupRoot, FileName);
        if (!File.Exists(path)) return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            int sep = line.IndexOf(" *", StringComparison.Ordinal);
            if (sep <= 0) continue;
            result[line[(sep + 2)..].Replace('/', '\\')] = line[..sep];
        }
        return result;
    }

    public static async Task<string> ComputeAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan | FileOptions.Asynchronous);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }
}
