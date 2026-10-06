using DanBackup.Core.Model;

namespace DanBackup.App.ViewModels;

/// <summary>Escolha e leitura de um backup existente (abas Restaurar e Verificar).</summary>
public static class BackupFolderPicker
{
    /// <summary>Pede a pasta; se o usuário escolher a pasta "pai", usa o backup mais recente dentro dela.</summary>
    public static string? Pick(string? initial)
    {
        var folder = Dialogs.PickFolder("Escolha a pasta do backup (com manifest.json)", initial);
        if (folder is null || BackupManifest.Exists(folder)) return folder;

        var inner = Directory.EnumerateDirectories(folder).Where(BackupManifest.Exists).OrderDescending().FirstOrDefault();
        if (inner is null)
            Dialogs.Warning("Esta pasta não contém um backup do DanBackup (manifest.json não encontrado).");
        return inner;
    }

    public static BackupManifest? Load(string folder)
    {
        try
        {
            return BackupManifest.Load(folder);
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Não foi possível ler o manifesto: {ex.Message}");
            return null;
        }
    }

    public static string Describe(BackupManifest manifest) =>
        $"Backup de {manifest.CreatedAt:dd/MM/yyyy HH:mm} · computador {manifest.MachineName} · usuário {manifest.UserName} · {manifest.OsVersion}" +
        (manifest.Encryption is not null ? " · 🔒 com itens criptografados" : "") +
        (manifest.Cancelled ? " · (backup foi cancelado — pode estar incompleto)" : "");
}
