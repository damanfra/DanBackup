using System.Diagnostics;
using System.Security.Cryptography;
using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Engine;

/// <summary>
/// Teste de recuperação de um backup, somente leitura (não altera o sistema nem o backup, exceto pelo log):
/// 1) senha confere; 2) cada arquivo existe e bate com o checksums.sha256;
/// 3) cada .dbenc descriptografa (o AES-GCM detecta qualquer alteração); 4) checagens específicas de cada módulo.
/// Status do item: Success = recuperável; Warning = recuperável com ressalvas; Failed = não recuperável.
/// </summary>
public sealed class VerifyEngine(AppSettings settings, IOperationReporter reporter, string? password)
{
    public async Task RunAsync(string backupRoot, IReadOnlyList<ModulePlan> plan, CancellationToken ct)
    {
        using var log = new FileLogReporter(Path.Combine(backupRoot, $"verificacao_{DateTime.Now:yyyy-MM-dd_HHmmss}.log"), reporter);
        var manifest = BackupManifest.Load(backupRoot);
        log.Log(LogLevel.Info, $"Verificando backup de {manifest.CreatedAt:dd/MM/yyyy HH:mm} ({manifest.MachineName})");

        if (manifest.Cancelled)
            log.Log(LogLevel.Warning, "O backup foi cancelado: itens não salvos aparecerão como falha.");
        else if (manifest.CompletedAt is null)
            log.Log(LogLevel.Warning, "O backup não chegou ao fim (interrompido?).");

        var crypto = RestoreEngine.OpenCrypto(manifest, password);
        if (crypto is not null)
            log.Log(LogLevel.Info, "Senha conferida.");

        var checksums = Checksums.Load(backupRoot);
        if (checksums is null)
            log.Log(LogLevel.Warning, $"Backup sem {Checksums.FileName} (feito por uma versão anterior): só dá para conferir se os arquivos abrem, não se estão íntegros.");

        int total = plan.Sum(p => p.Items.Count), done = 0;
        log.Progress(0, total);

        foreach (var (module, items) in plan)
        {
            if (items.Count == 0) continue;
            log.Log(LogLevel.Info, $"== {module.Name} ==");
            var ctx = new OperationContext
            {
                ModuleDir = Path.Combine(backupRoot, "modules", module.Id),
                Settings = settings,
                Reporter = log,
                Crypto = crypto,
            };

            foreach (var item in items)
            {
                var backupStatus = item.Status;
                var backupMessage = item.Message;
                await ItemRunner.RunAsync(item, ctx, async () =>
                {
                    CheckBackupStatus(backupStatus, backupMessage);
                    if (await VerifyFilesAsync(item, ctx, backupRoot, checksums, ct))
                        await module.VerifyItemAsync(item, ctx, ct);
                }, ct);
                log.Progress(++done, total);
            }
        }

        log.Log(LogLevel.Info, "Verificação concluída.");
    }

    private static void CheckBackupStatus(ItemStatus status, string? message)
    {
        switch (status)
        {
            case ItemStatus.Failed:
                throw new InvalidOperationException($"O backup deste item falhou: {message}");
            case ItemStatus.Skipped:
            case ItemStatus.Pending:
                throw new InvalidOperationException("Este item não chegou a ser salvo (backup cancelado?).");
        }
    }

    /// <summary>Confere existência, hash e descriptografia. Retorna false se não houver nada salvo.</summary>
    private static async Task<bool> VerifyFilesAsync(BackupItem item, OperationContext ctx, string backupRoot,
        Dictionary<string, string>? checksums, CancellationToken ct)
    {
        var dir = ctx.GetItemDir(item);
        var prefix = Path.GetRelativePath(backupRoot, dir) + "\\";
        var expected = checksums?.Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories) : [];

        if (files.Length == 0 && (expected is null || expected.Count == 0))
        {
            ctx.Warn("Nenhum arquivo foi salvo para este item.");
            return false;
        }

        var problems = new List<string>();
        var throttle = Stopwatch.StartNew();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(backupRoot, file);
            if (throttle.ElapsedMilliseconds > 200)
            {
                ctx.Status($"Conferindo {relative}");
                throttle.Restart();
            }

            if (expected is not null)
            {
                if (!expected.Remove(relative, out var hash))
                    ctx.Info($"Arquivo não listado nos checksums (adicionado depois?): {relative}");
                else if (!string.Equals(hash, await Checksums.ComputeAsync(file, ct), StringComparison.OrdinalIgnoreCase))
                    problems.Add($"alterado ou corrompido: {relative}");
            }

            if (file.EndsWith(BackupCrypto.EncryptedExtension, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ctx.RequireCrypto().Decrypt(await File.ReadAllBytesAsync(file, ct));
                }
                catch (CryptographicException)
                {
                    problems.Add($"não descriptografa: {relative}");
                }
            }
        }

        if (expected is not null)
            problems.AddRange(expected.Keys.Select(k => $"faltando: {k}"));

        if (problems.Count > 0)
        {
            foreach (var p in problems) ctx.Reporter.Log(LogLevel.Error, $"{item.DisplayName}: {p}");
            throw new InvalidDataException(problems.Count == 1 ? problems[0] : $"{problems.Count} problemas (primeiro: {problems[0]})");
        }

        ctx.Info($"{item.DisplayName}: {files.Length} arquivo(s) íntegros.");
        return true;
    }
}
