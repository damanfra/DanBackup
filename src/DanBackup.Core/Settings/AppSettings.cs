using System.Text.Json;

namespace DanBackup.Core.Settings;

public sealed class CustomGame
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>
/// Preferências do usuário, salvas em %APPDATA%\DanBackup\settings.json.
/// </summary>
public sealed class AppSettings
{
    public static readonly string[] DefaultGameConfigExtensions =
    [
        ".ini", ".cfg", ".conf", ".config", ".xml", ".json", ".vdf", ".txt",
        ".settings", ".lua", ".yaml", ".yml", ".prefs", ".properties", ".wtf",
    ];

    public string? LastDestination { get; set; }

    /// <summary>Senha do backup protegida com DPAPI (ver PasswordStore). Nunca em texto puro.</summary>
    public string? ProtectedPassword { get; set; }

    /// <summary>Pastas extras escolhidas pelo usuário.</summary>
    public List<string> CustomFolders { get; set; } = [];

    /// <summary>Chaves do registro extras (ex.: HKCU\Software\SimonTatham\PuTTY).</summary>
    public List<string> CustomRegistryKeys { get; set; } = [];

    /// <summary>Jogos que a varredura automática não encontra.</summary>
    public List<CustomGame> CustomGames { get; set; } = [];

    /// <summary>Extensões consideradas "arquivo de configuração" na cópia de jogos.</summary>
    public List<string> GameConfigExtensions { get; set; } = [.. DefaultGameConfigExtensions];

    public int GameMaxFileSizeMB { get; set; } = 5;

    /// <summary>Copia a pasta inteira do jogo (inclui saves), não só configurações.</summary>
    public bool GameIncludeAllFiles { get; set; }

    /// <summary>Ignora arquivos que estão só na nuvem (OneDrive "sob demanda") para não forçar o download.</summary>
    public bool SkipCloudOnlyFiles { get; set; } = true;

    /// <summary>Nomes de pastas nunca copiadas nas pastas do usuário.</summary>
    public List<string> ExcludedFolderNames { get; set; } = ["node_modules", "$RECYCLE.BIN", "System Volume Information"];
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DanBackup");

    public static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new();
        }
        catch (JsonException)
        {
            // Arquivo corrompido: começa do padrão.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
