using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanBackup.Core.IO;
using DanBackup.Core.Platform;
using DanBackup.Core.Settings;

namespace DanBackup.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public SettingsViewModel(AppSettings settings)
    {
        _settings = settings;
        LoadFrom(settings);
    }

    public ObservableCollection<string> CustomFolders { get; } = [];
    public ObservableCollection<string> CustomRegistryKeys { get; } = [];
    public ObservableCollection<CustomGame> CustomGames { get; } = [];

    [ObservableProperty] private string _gameExtensionsText = "";
    [ObservableProperty] private int _gameMaxFileSizeMB;
    [ObservableProperty] private bool _gameIncludeAllFiles;
    [ObservableProperty] private bool _skipCloudOnlyFiles;
    [ObservableProperty] private string _excludedFoldersText = "";

    [ObservableProperty] private string _newRegistryKey = "";
    [ObservableProperty] private string _newGameName = "";
    [ObservableProperty] private string? _selectedFolder;
    [ObservableProperty] private string? _selectedRegistryKey;
    [ObservableProperty] private CustomGame? _selectedGame;
    [ObservableProperty] private string _savedMessage = "";

    public string SettingsPath => SettingsStore.SettingsPath;

    private void LoadFrom(AppSettings s)
    {
        Reset(CustomFolders, s.CustomFolders);
        Reset(CustomRegistryKeys, s.CustomRegistryKeys);
        Reset(CustomGames, s.CustomGames);
        GameExtensionsText = string.Join(" ", s.GameConfigExtensions);
        GameMaxFileSizeMB = s.GameMaxFileSizeMB;
        GameIncludeAllFiles = s.GameIncludeAllFiles;
        SkipCloudOnlyFiles = s.SkipCloudOnlyFiles;
        ExcludedFoldersText = string.Join("; ", s.ExcludedFolderNames);
    }

    private static void Reset<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var x in source) target.Add(x);
    }

    [RelayCommand]
    private void AddFolder()
    {
        var folder = Dialogs.PickFolder("Pasta extra para incluir no backup");
        if (folder is null) return;
        var tokenized = PathTokens.Tokenize(folder);
        if (!CustomFolders.Contains(tokenized, StringComparer.OrdinalIgnoreCase)) CustomFolders.Add(tokenized);
    }

    [RelayCommand]
    private void RemoveFolder()
    {
        if (SelectedFolder is not null) CustomFolders.Remove(SelectedFolder);
    }

    [RelayCommand]
    private void AddRegistryKey()
    {
        var key = NewRegistryKey.Trim();
        if (key.Length == 0) return;
        if (RegistryHelper.Parse(key) is null)
        {
            Dialogs.Warning("Use o formato HKCU\\Software\\... ou HKLM\\SOFTWARE\\...");
            return;
        }
        if (!RegistryHelper.KeyExists(key))
            Dialogs.Warning("Essa chave não existe neste computador agora; ela será ignorada até existir.");
        if (!CustomRegistryKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) CustomRegistryKeys.Add(key);
        NewRegistryKey = "";
    }

    [RelayCommand]
    private void RemoveRegistryKey()
    {
        if (SelectedRegistryKey is not null) CustomRegistryKeys.Remove(SelectedRegistryKey);
    }

    [RelayCommand]
    private void AddGame()
    {
        var name = NewGameName.Trim();
        if (name.Length == 0)
        {
            Dialogs.Warning("Digite o nome do jogo primeiro.");
            return;
        }
        var folder = Dialogs.PickFolder($"Pasta onde \"{name}\" guarda as configurações");
        if (folder is null) return;
        CustomGames.Add(new CustomGame { Name = name, Path = PathTokens.Tokenize(folder) });
        NewGameName = "";
    }

    [RelayCommand]
    private void RemoveGame()
    {
        if (SelectedGame is not null) CustomGames.Remove(SelectedGame);
    }

    [RelayCommand]
    private void ResetExtensions() =>
        GameExtensionsText = string.Join(" ", AppSettings.DefaultGameConfigExtensions);

    [RelayCommand]
    private void Save()
    {
        _settings.CustomFolders = [.. CustomFolders];
        _settings.CustomRegistryKeys = [.. CustomRegistryKeys];
        _settings.CustomGames = [.. CustomGames];
        _settings.GameConfigExtensions = GameExtensionsText
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Distinct()
            .ToList();
        _settings.GameMaxFileSizeMB = Math.Max(1, GameMaxFileSizeMB);
        _settings.GameIncludeAllFiles = GameIncludeAllFiles;
        _settings.SkipCloudOnlyFiles = SkipCloudOnlyFiles;
        _settings.ExcludedFolderNames = ExcludedFoldersText
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        SettingsStore.Save(_settings);
        LoadFrom(_settings);
        SavedMessage = $"Salvo às {DateTime.Now:HH:mm:ss}. Clique em \"Analisar\" na aba Backup para aplicar.";
    }
}
