using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Modules;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Engine;

public sealed record ModulePlan(IBackupModule Module, IReadOnlyList<BackupItem> Items);

/// <summary>
/// Executa os módulos selecionados, gravando em &lt;backupRoot&gt;/modules/&lt;id&gt; e mantendo o manifest.json atualizado.
/// </summary>
/// <param name="password">Senha usada para criptografar os itens sensíveis (Wi-Fi, certificados, chaves...).</param>
public sealed class BackupEngine(AppSettings settings, IOperationReporter reporter, string password)
{
    public async Task<BackupManifest> RunAsync(string backupRoot, IReadOnlyList<ModulePlan> plan, CancellationToken ct)
    {
        var (crypto, encryption) = BackupCrypto.Create(password);
        Directory.CreateDirectory(backupRoot);
        using var log = new FileLogReporter(Path.Combine(backupRoot, "backup.log"), reporter);

        var manifest = BackupManifest.CreateForCurrentMachine();
        manifest.Encryption = encryption;
        manifest.Save(backupRoot);
        log.Log(LogLevel.Info, $"Backup iniciado em {backupRoot} ({manifest.OsVersion})");

        int total = plan.Sum(p => p.Items.Count);
        int done = 0;
        log.Progress(0, total);

        try
        {
            foreach (var (module, planItems) in plan)
            {
                if (planItems.Count == 0) continue;
                var items = planItems.Select(i => i.Clone()).ToList();
                var mm = new ModuleManifest { Id = module.Id, Name = module.Name };
                manifest.Modules.Add(mm);

                var ctx = new OperationContext
                {
                    ModuleDir = Path.Combine(backupRoot, "modules", module.Id),
                    Settings = settings,
                    Reporter = log,
                    ExcludedRoot = backupRoot,
                    Crypto = crypto,
                };
                Directory.CreateDirectory(ctx.ModuleDir);
                log.Log(LogLevel.Info, $"== {module.Name} ==");

                try
                {
                    await module.BeforeBackupAsync(items, ctx, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.Log(LogLevel.Error, $"{module.Name}: {ex.Message}");
                    foreach (var item in items)
                    {
                        item.Status = ItemStatus.Failed;
                        item.Message = ex.Message;
                        mm.Items.Add(item);
                    }
                    done += items.Count;
                    log.Progress(done, total);
                    manifest.Save(backupRoot);
                    continue;
                }

                foreach (var item in items)
                {
                    mm.Items.Add(item);
                    await ItemRunner.RunAsync(item, ctx, () => module.BackupItemAsync(item, ctx, ct), ct);
                    log.Progress(++done, total);
                }
                manifest.Save(backupRoot);
            }

            // Hash de tudo que foi gravado, para a verificação detectar arquivos faltando/corrompidos.
            await Checksums.WriteAsync(backupRoot, log, ct);
        }
        catch (OperationCanceledException)
        {
            manifest.Cancelled = true;
            log.Log(LogLevel.Warning, "Backup cancelado pelo usuário.");
            throw;
        }
        finally
        {
            manifest.CompletedAt = DateTimeOffset.Now;
            manifest.Save(backupRoot);
        }

        log.Log(LogLevel.Info, "Backup concluído.");
        return manifest;
    }
}

/// <summary>Executa um item, traduzindo exceções/avisos em status.</summary>
internal static class ItemRunner
{
    public static async Task RunAsync(BackupItem item, OperationContext ctx, Func<Task> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ctx.Status(item.DisplayName);
        ctx.ResetWarnings();
        try
        {
            await action();
            item.Status = ctx.WarningCount > 0 ? ItemStatus.Warning : ItemStatus.Success;
            item.Message = ctx.WarningCount switch
            {
                0 => null,
                1 => ctx.LastWarning,
                _ => $"{ctx.WarningCount} avisos (último: {ctx.LastWarning})",
            };
        }
        catch (OperationCanceledException)
        {
            item.Status = ItemStatus.Skipped;
            item.Message = "Cancelado";
            throw;
        }
        catch (Exception ex)
        {
            item.Status = ItemStatus.Failed;
            item.Message = ex.Message;
            ctx.Reporter.Log(LogLevel.Error, $"{item.DisplayName}: {ex.Message}");
        }
    }
}
