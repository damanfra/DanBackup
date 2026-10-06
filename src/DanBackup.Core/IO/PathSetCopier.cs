using DanBackup.Core.Engine;
using DanBackup.Core.Platform;
using DanBackup.Core.Security;

namespace DanBackup.Core.IO;

/// <summary>
/// Salva/restaura um conjunto de caminhos (com tokens e curingas) e chaves de registro.
/// Os arquivos ficam em &lt;itemDir&gt;\files\&lt;caminho espelhado&gt; e o registro em &lt;itemDir&gt;\registry\*.reg,
/// então a restauração não precisa de nenhum mapeamento extra.
/// Itens sensíveis são gravados criptografados (*.dbenc) e descriptografados automaticamente na restauração.
/// </summary>
public static class PathSetCopier
{
    public const string FilesDir = "files";
    public const string RegistryDir = "registry";

    /// <param name="directoryOptions">Opções para arquivos dentro de pastas; arquivos citados explicitamente são sempre copiados.</param>
    /// <param name="sensitive">Criptografa tudo (arquivos e .reg).</param>
    public static async Task<CopyStats> BackupAsync(
        IEnumerable<string> patterns,
        IEnumerable<string> registryKeys,
        string itemDir,
        CopyOptions directoryOptions,
        OperationContext ctx,
        CancellationToken ct,
        bool sensitive = false)
    {
        var total = new CopyStats();
        var filesRoot = Path.Combine(itemDir, FilesDir);
        var fileMode = sensitive ? CryptoMode.Encrypt : CryptoMode.None;
        if (sensitive)
        {
            directoryOptions = new CopyOptions
            {
                FileFilter = directoryOptions.FileFilter,
                ExcludedDirNames = directoryOptions.ExcludedDirNames,
                SkipCloudOnlyFiles = directoryOptions.SkipCloudOnlyFiles,
                Crypto = CryptoMode.Encrypt,
            };
        }

        foreach (var pattern in patterns)
        {
            ct.ThrowIfCancellationRequested();
            var expanded = PathTokens.Expand(pattern);
            if (expanded is null)
            {
                ctx.Info($"Local não existe nesta máquina: {pattern}");
                continue;
            }

            var hint = PathTokens.LeadingToken(pattern);
            string[] hints = hint is null ? [] : [hint];

            foreach (var path in GlobResolver.Resolve(expanded))
            {
                var destination = Path.Combine(filesRoot, MirrorPath.ToRelative(path, hints));
                if (File.Exists(path))
                {
                    if (FileCopier.CopyFile(path, destination, ctx, fileMode))
                    {
                        total.Files++;
                        total.Bytes += new FileInfo(path).Length;
                    }
                }
                else
                {
                    var stats = FileCopier.CopyDirectory(path, destination, directoryOptions, ctx, ct);
                    total.Files += stats.Files;
                    total.Bytes += stats.Bytes;
                }
            }
        }

        int index = 0;
        foreach (var key in registryKeys)
        {
            ct.ThrowIfCancellationRequested();
            if (!RegistryHelper.KeyExists(key)) continue;
            var file = Path.Combine(itemDir, RegistryDir, $"{index++:00}_{PathUtil.SafeName(key.Split('\\').Last())}.reg");
            await RegistryHelper.ExportAsync(key, file, ct);
            if (sensitive)
            {
                ctx.RequireCrypto().EncryptFile(file, file + BackupCrypto.EncryptedExtension);
                File.Delete(file);
            }
            total.Files++;
        }

        if (total.Files == 0)
            ctx.Warn("Nenhum arquivo encontrado para salvar.");
        else
            ctx.Info($"Salvo{(sensitive ? " (criptografado)" : "")}: {total}");
        return total;
    }

    public static async Task RestoreAsync(string itemDir, OperationContext ctx, CancellationToken ct)
    {
        var filesRoot = Path.Combine(itemDir, FilesDir);
        if (Directory.Exists(filesRoot))
        {
            foreach (var entry in new DirectoryInfo(filesRoot).EnumerateFileSystemInfos())
            {
                var target = MirrorPath.ToAbsolute(entry.Name);
                if (target is null)
                {
                    ctx.Warn($"Não sei para onde restaurar '{entry.Name}' nesta máquina (programa não instalado?).");
                    continue;
                }

                if (entry is FileInfo)
                {
                    FileCopier.CopyFile(entry.FullName, target, ctx, CryptoMode.Decrypt);
                }
                else
                {
                    var stats = FileCopier.CopyDirectory(entry.FullName, target, CopyOptions.Restore, ctx, ct);
                    ctx.Info($"Restaurado em {target}: {stats}");
                }
            }
        }

        var regRoot = Path.Combine(itemDir, RegistryDir);
        if (Directory.Exists(regRoot))
        {
            foreach (var reg in Directory.EnumerateFiles(regRoot).Order())
            {
                ct.ThrowIfCancellationRequested();
                bool encrypted = reg.EndsWith(".reg" + BackupCrypto.EncryptedExtension, StringComparison.OrdinalIgnoreCase);
                if (!encrypted && !reg.EndsWith(".reg", StringComparison.OrdinalIgnoreCase)) continue;

                // .reg criptografado: descriptografa para um temporário só durante a importação.
                var file = encrypted ? Path.Combine(Path.GetTempPath(), $"danbackup-{Guid.NewGuid():N}.reg") : reg;
                try
                {
                    if (encrypted) ctx.RequireCrypto().DecryptFile(reg, file);
                    await RegistryHelper.ImportAsync(file, ct);
                    ctx.Info($"Registro importado: {Path.GetFileName(reg)}");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException)
                {
                    ctx.Warn(ex.Message);
                }
                finally
                {
                    if (encrypted) File.Delete(file);
                }
            }
        }
    }
}
