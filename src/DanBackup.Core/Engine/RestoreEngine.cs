using DanBackup.Core.Model;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Engine;

/// <summary>
/// Restaura somente os itens explicitamente escolhidos pelo usuário.
/// </summary>
/// <param name="password">Senha do backup; obrigatória se o backup tiver itens criptografados.</param>
public sealed class RestoreEngine(AppSettings settings, IOperationReporter reporter, string? password)
{
    public static string LogsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DanBackup", "logs");

    /// <summary>Valida a senha contra o manifesto. Lança <see cref="InvalidPasswordException"/> se estiver errada.</summary>
    public static BackupCrypto? OpenCrypto(BackupManifest manifest, string? password)
    {
        if (manifest.Encryption is null) return null;
        if (string.IsNullOrEmpty(password)) throw new InvalidPasswordException();
        return BackupCrypto.Open(manifest.Encryption, password);
    }

    public async Task RunAsync(string backupRoot, IReadOnlyList<ModulePlan> plan, CancellationToken ct)
    {
        // Falha antes de restaurar qualquer coisa se a senha estiver errada.
        var crypto = OpenCrypto(BackupManifest.Load(backupRoot), password);

        Directory.CreateDirectory(LogsDir);
        var logPath = Path.Combine(LogsDir, $"restore_{DateTime.Now:yyyy-MM-dd_HHmmss}.log");
        using var log = new FileLogReporter(logPath, reporter);
        log.Log(LogLevel.Info, $"Restauração a partir de {backupRoot} (log: {logPath})");

        int total = plan.Sum(p => p.Items.Count);
        int done = 0;
        log.Progress(0, total);

        try
        {
            foreach (var (module, items) in plan)
            {
                if (items.Count == 0) continue;
                var ctx = new OperationContext
                {
                    ModuleDir = Path.Combine(backupRoot, "modules", module.Id),
                    Settings = settings,
                    Reporter = log,
                    Crypto = crypto,
                };
                log.Log(LogLevel.Info, $"== {module.Name} ==");

                try
                {
                    await module.BeforeRestoreAsync(items, ctx, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.Log(LogLevel.Error, $"{module.Name}: {ex.Message}");
                    foreach (var item in items)
                    {
                        item.Status = ItemStatus.Failed;
                        item.Message = ex.Message;
                    }
                    done += items.Count;
                    log.Progress(done, total);
                    continue;
                }

                foreach (var item in items)
                {
                    await ItemRunner.RunAsync(item, ctx, () => module.RestoreItemAsync(item, ctx, ct), ct);
                    log.Progress(++done, total);
                }
            }
        }
        catch (OperationCanceledException)
        {
            log.Log(LogLevel.Warning, "Restauração cancelada pelo usuário.");
            throw;
        }

        log.Log(LogLevel.Info, "Restauração concluída.");
    }
}
