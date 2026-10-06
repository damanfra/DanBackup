using System.Security.Cryptography;
using System.Text;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Security;

/// <summary>
/// Guarda a senha do backup nas configurações protegida pelo DPAPI do Windows:
/// só este usuário, neste computador, consegue lê-la. Depois de formatar, ela precisa ser digitada de novo.
/// </summary>
public static class PasswordStore
{
    private static readonly byte[] Entropy = "DanBackup.Password.v1"u8.ToArray();

    public static string? Load(AppSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ProtectedPassword)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(settings.ProtectedPassword), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null; // gravada em outro usuário/máquina
        }
    }

    public static void Remember(AppSettings settings, string password) =>
        settings.ProtectedPassword = Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));

    public static void Forget(AppSettings settings) => settings.ProtectedPassword = null;
}
