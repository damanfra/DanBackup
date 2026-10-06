namespace DanBackup.Core.Modules;

public static class ModuleRegistry
{
    /// <summary>Todos os módulos, na ordem em que aparecem na tela.</summary>
    public static IReadOnlyList<IBackupModule> CreateAll() =>
    [
        new UserFoldersModule(),
        new BrowsersModule(),
        new GamesModule(),
        new AppConfigsModule(),
        new OfficeModule(),
        new WifiModule(),
        new ProgramsModule(),
        new DriversModule(),
        new PrintersModule(),
        new FontsModule(),
        new CertificatesModule(),
        new EnvironmentModule(),
        new RegistryKeysModule(),
    ];
}
