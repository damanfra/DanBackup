using System.Text.Json;
using System.Text.Json.Serialization;
using DanBackup.Core.Platform;
using DanBackup.Core.Security;

namespace DanBackup.Core.Model;

public sealed class ModuleManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public List<BackupItem> Items { get; init; } = [];
}

/// <summary>
/// Descreve o conteúdo de um backup. Fica na raiz da pasta do backup como manifest.json.
/// </summary>
public sealed class BackupManifest
{
    public const string FileName = "manifest.json";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string App { get; set; } = "DanBackup";
    public int FormatVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool Cancelled { get; set; }
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string OsVersion { get; set; } = "";

    /// <summary>Presente quando o backup tem arquivos criptografados.</summary>
    public EncryptionInfo? Encryption { get; set; }

    public List<ModuleManifest> Modules { get; set; } = [];

    public static BackupManifest CreateForCurrentMachine() => new()
    {
        CreatedAt = DateTimeOffset.Now,
        MachineName = Environment.MachineName,
        UserName = Environment.UserName,
        OsVersion = SystemInfo.GetOsDescription(),
    };

    public static bool Exists(string backupRoot) => File.Exists(Path.Combine(backupRoot, FileName));

    public void Save(string backupRoot)
    {
        var path = Path.Combine(backupRoot, FileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    public static BackupManifest Load(string backupRoot)
    {
        var json = File.ReadAllText(Path.Combine(backupRoot, FileName));
        return JsonSerializer.Deserialize<BackupManifest>(json, JsonOptions)
               ?? throw new InvalidDataException("Manifesto inválido.");
    }
}
