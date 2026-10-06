using System.Diagnostics;
using System.Security.Cryptography;
using DanBackup.Core.Engine;
using DanBackup.Core.Security;

namespace DanBackup.Core.IO;

public enum CryptoMode
{
    /// <summary>Cópia simples.</summary>
    None,

    /// <summary>Backup: grava criptografado, com a extensão .dbenc.</summary>
    Encrypt,

    /// <summary>Restauração: arquivos .dbenc são descriptografados (e perdem a extensão); os demais são copiados.</summary>
    Decrypt,
}

public sealed class CopyOptions
{
    public CryptoMode Crypto { get; init; } = CryptoMode.None;

    /// <summary>Retorna false para pular o arquivo.</summary>
    public Func<FileInfo, bool>? FileFilter { get; init; }

    /// <summary>Nomes de pastas ignoradas em qualquer nível.</summary>
    public IReadOnlySet<string> ExcludedDirNames { get; init; } = new HashSet<string>();

    public bool SkipCloudOnlyFiles { get; init; } = true;

    public static CopyOptions Everything { get; } = new() { SkipCloudOnlyFiles = false };

    /// <summary>Tudo, descriptografando os arquivos .dbenc — para restaurações.</summary>
    public static CopyOptions Restore { get; } = new() { SkipCloudOnlyFiles = false, Crypto = CryptoMode.Decrypt };
}

public sealed class CopyStats
{
    public int Files { get; set; }
    public long Bytes { get; set; }
    public int Failed { get; set; }
    public int SkippedCloudOnly { get; set; }

    public override string ToString() => $"{Files} arquivo(s), {PathUtil.FormatSize(Bytes)}";
}

public static class FileCopier
{
    // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS / RECALL_ON_OPEN: arquivo só existe na nuvem (OneDrive etc.)
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    private static readonly EnumerationOptions Enumeration = new()
    {
        IgnoreInaccessible = true,
        // Não pula ocultos/sistema (ex.: .ssh), mas pula junctions como "Meus Documentos\Minhas Músicas".
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false,
    };

    public static CopyStats CopyDirectory(string source, string destination, CopyOptions options, OperationContext ctx, CancellationToken ct)
    {
        var stats = new CopyStats();
        var throttle = Stopwatch.StartNew();
        CopyDirectoryCore(new DirectoryInfo(source), destination, options, ctx, stats, throttle, ct);
        if (stats.SkippedCloudOnly > 0)
            ctx.Info($"{stats.SkippedCloudOnly} arquivo(s) somente-nuvem ignorados em {source}.");
        return stats;
    }

    private static void CopyDirectoryCore(DirectoryInfo dir, string destination, CopyOptions options,
        OperationContext ctx, CopyStats stats, Stopwatch throttle, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ctx.ExcludedRoot is not null && PathUtil.IsSameOrUnder(dir.FullName, ctx.ExcludedRoot))
            return;

        FileInfo[] files;
        DirectoryInfo[] subdirs;
        try
        {
            files = dir.GetFiles("*", Enumeration);
            subdirs = dir.GetDirectories("*", Enumeration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ctx.Warn($"Sem acesso a {dir.FullName}: {ex.Message}");
            stats.Failed++;
            return;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (options.FileFilter is not null && !options.FileFilter(file)) continue;
            if (options.SkipCloudOnlyFiles && IsCloudOnly(file))
            {
                stats.SkippedCloudOnly++;
                continue;
            }
            if (throttle.ElapsedMilliseconds > 200)
            {
                ctx.Status(file.FullName);
                throttle.Restart();
            }
            if (CopyFile(file.FullName, Path.Combine(destination, file.Name), ctx, options.Crypto))
            {
                stats.Files++;
                stats.Bytes += file.Length;
            }
            else
            {
                stats.Failed++;
            }
        }

        foreach (var sub in subdirs)
        {
            if (options.ExcludedDirNames.Contains(sub.Name)) continue;
            CopyDirectoryCore(sub, Path.Combine(destination, sub.Name), options, ctx, stats, throttle, ct);
        }
    }

    /// <summary>Copia um arquivo; se estiver em uso, tenta ler com compartilhamento. Retorna false (com aviso) se falhar.</summary>
    public static bool CopyFile(string source, string destination, OperationContext ctx, CryptoMode crypto = CryptoMode.None)
    {
        if (crypto == CryptoMode.Encrypt)
            return Transform(source, destination + BackupCrypto.EncryptedExtension, ctx, c => c.EncryptFile);
        if (crypto == CryptoMode.Decrypt && source.EndsWith(BackupCrypto.EncryptedExtension, StringComparison.OrdinalIgnoreCase))
            return Transform(source, destination[..^BackupCrypto.EncryptedExtension.Length], ctx, c => c.DecryptFile);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
                File.SetAttributes(destination, FileAttributes.Normal);
            File.Copy(source, destination, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            // Provavelmente em uso por outro programa: tenta uma cópia compartilhada.
            try
            {
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write))
                    input.CopyTo(output);
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ctx.Warn($"Não foi possível copiar {source}: {ex.Message}");
                return false;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            ctx.Warn($"Sem permissão para copiar {source}: {ex.Message}");
            return false;
        }
    }

    private static bool Transform(string source, string destination, OperationContext ctx,
        Func<BackupCrypto, Action<string, string>> operation)
    {
        try
        {
            if (File.Exists(destination))
                File.SetAttributes(destination, FileAttributes.Normal);
            operation(ctx.RequireCrypto())(source, destination);
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
            return true;
        }
        catch (CryptographicException ex)
        {
            ctx.Warn($"Não foi possível descriptografar {source} (senha errada ou arquivo corrompido): {ex.Message}");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ctx.Warn($"Não foi possível copiar {source}: {ex.Message}");
            return false;
        }
    }

    private static bool IsCloudOnly(FileInfo file) =>
        (file.Attributes & (RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline)) != 0;
}
