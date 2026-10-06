using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Engine;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

public interface IOperationReporter
{
    void Log(LogLevel level, string message);
    void Status(string text);
    void Progress(int done, int total);
}

public sealed class NullReporter : IOperationReporter
{
    public static readonly NullReporter Instance = new();
    public void Log(LogLevel level, string message) { }
    public void Status(string text) { }
    public void Progress(int done, int total) { }
}

/// <summary>
/// O que um módulo recebe ao salvar/restaurar: onde gravar, configurações e para onde reportar.
/// </summary>
public sealed class OperationContext
{
    public required string ModuleDir { get; init; }
    public required AppSettings Settings { get; init; }
    public required IOperationReporter Reporter { get; init; }

    /// <summary>Pasta que nunca deve ser copiada (o próprio destino do backup).</summary>
    public string? ExcludedRoot { get; init; }

    /// <summary>Criptografia dos itens sensíveis. Null ao restaurar um backup antigo sem criptografia.</summary>
    public BackupCrypto? Crypto { get; init; }

    public BackupCrypto RequireCrypto() =>
        Crypto ?? throw new InvalidOperationException("Este item é criptografado: informe a senha do backup.");

    public int WarningCount { get; private set; }
    public string? LastWarning { get; private set; }

    public string GetItemDir(BackupItem item) => Path.Combine(ModuleDir, PathUtil.SafeName(item.Id));

    public void Info(string message) => Reporter.Log(LogLevel.Info, message);

    public void Warn(string message)
    {
        WarningCount++;
        LastWarning = message;
        Reporter.Log(LogLevel.Warning, message);
    }

    public void Status(string text) => Reporter.Status(text);

    internal void ResetWarnings()
    {
        WarningCount = 0;
        LastWarning = null;
    }
}
