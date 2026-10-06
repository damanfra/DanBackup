using System.Runtime.InteropServices;
using System.Text.Json;
using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Platform;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;
using Microsoft.Win32;

namespace DanBackup.Core.Modules;

/// <summary>Variáveis de ambiente do usuário e do sistema.</summary>
public sealed class EnvironmentModule : BackupModuleBase
{
    private const string UserKey = "Environment";
    private const string SystemKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const string PlainFile = "variaveis.json";
    private const string EncryptedFile = PlainFile + BackupCrypto.EncryptedExtension;

    // Variáveis que contêm listas de pastas: na restauração, as entradas faltantes são acrescentadas.
    private static readonly HashSet<string> ListVariables = new(StringComparer.OrdinalIgnoreCase) { "Path", "PSModulePath", "PATHEXT" };

    public sealed record EnvVar(string Name, string Value, bool Expandable);

    public override string Id => "environment";
    public override string Name => "Variáveis de ambiente";
    public override string Description => "Variáveis do usuário e do sistema (incluindo o PATH).";

    public override string? Notes =>
        "🔒 Salvas criptografadas (podem conter tokens). Na restauração, só variáveis que não existem são criadas; no PATH, as pastas que faltam são acrescentadas. Nada é sobrescrito.";

    public override Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        IReadOnlyList<BackupItem> items =
        [
            new BackupItem { Id = "user", DisplayName = "Variáveis do usuário", Description = $"{Read(Registry.CurrentUser, UserKey).Count} variáveis" },
            new BackupItem
            {
                Id = "system", DisplayName = "Variáveis do sistema", SelectedByDefault = true,
                Description = $"{Read(Registry.LocalMachine, SystemKey).Count} variáveis (restaurar exige administrador)",
            },
        ];
        return Task.FromResult(items);
    }

    public override async Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var vars = item.Id == "user" ? Read(Registry.CurrentUser, UserKey) : Read(Registry.LocalMachine, SystemKey);
        Directory.CreateDirectory(ctx.GetItemDir(item));
        // Variáveis costumam guardar tokens/chaves de API: sempre criptografadas.
        var json = JsonSerializer.SerializeToUtf8Bytes(vars, BackupManifest.JsonOptions);
        await File.WriteAllBytesAsync(Path.Combine(ctx.GetItemDir(item), EncryptedFile), ctx.RequireCrypto().Encrypt(json), ct);
        ctx.Info($"{vars.Count} variáveis salvas (criptografadas).");
    }

    public override Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var json = IO.SecureFile.ReadAllBytes(Path.Combine(ctx.GetItemDir(item), PlainFile), ctx)
                   ?? throw new FileNotFoundException("Arquivo de variáveis não encontrado.");
        var vars = JsonSerializer.Deserialize<List<EnvVar>>(json, BackupManifest.JsonOptions);
        ctx.Info($"{vars?.Count ?? 0} variáveis legíveis.");
        return Task.CompletedTask;
    }

    public override async Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        bool isSystem = item.Id == "system";
        if (isSystem && !SystemInfo.IsAdministrator())
            throw new UnauthorizedAccessException("Restaurar variáveis do sistema exige administrador.");

        var encrypted = Path.Combine(ctx.GetItemDir(item), EncryptedFile);
        var json = File.Exists(encrypted)
            ? ctx.RequireCrypto().Decrypt(await File.ReadAllBytesAsync(encrypted, ct))
            : await File.ReadAllBytesAsync(Path.Combine(ctx.GetItemDir(item), PlainFile), ct); // backups antigos
        var saved = JsonSerializer.Deserialize<List<EnvVar>>(json, BackupManifest.JsonOptions) ?? [];

        using var key = (isSystem ? Registry.LocalMachine : Registry.CurrentUser)
            .OpenSubKey(isSystem ? SystemKey : UserKey, writable: true)
            ?? throw new InvalidOperationException("Chave de variáveis de ambiente não encontrada.");

        int created = 0, merged = 0;
        foreach (var v in saved)
        {
            var kind = v.Expandable ? RegistryValueKind.ExpandString : RegistryValueKind.String;
            var current = key.GetValue(v.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

            if (current is null)
            {
                key.SetValue(v.Name, v.Value, kind);
                created++;
            }
            else if (ListVariables.Contains(v.Name))
            {
                var entries = current.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
                var missing = v.Value.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Where(e => !entries.Any(x => Normalize(x) == Normalize(e)))
                    .ToList();
                if (missing.Count > 0)
                {
                    key.SetValue(v.Name, string.Join(';', entries.Concat(missing)), RegistryValueKind.ExpandString);
                    ctx.Info($"{v.Name}: {missing.Count} entrada(s) acrescentada(s): {string.Join("; ", missing)}");
                    merged++;
                }
            }
            else if (!string.Equals(current, v.Value, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Info($"{v.Name} já existe com outro valor; mantido o atual. (backup: {v.Value})");
            }
        }

        BroadcastEnvironmentChange();
        ctx.Info($"{created} variável(is) criada(s), {merged} lista(s) mesclada(s).");
    }

    private static string Normalize(string entry) => entry.Trim().TrimEnd('\\').ToUpperInvariant();

    private static List<EnvVar> Read(RegistryKey hive, string subKey)
    {
        using var key = hive.OpenSubKey(subKey);
        if (key is null) return [];
        return key.GetValueNames()
            .Select(name => new EnvVar(
                name,
                key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "",
                key.GetValueKind(name) == RegistryValueKind.ExpandString))
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint flags, uint timeout, out UIntPtr result);

    private static void BroadcastEnvironmentChange()
    {
        const uint WM_SETTINGCHANGE = 0x001A, SMTO_ABORTIFHUNG = 0x0002;
        SendMessageTimeout(new IntPtr(0xFFFF), WM_SETTINGCHANGE, UIntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 3000, out _);
    }
}
