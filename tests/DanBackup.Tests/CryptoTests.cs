using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;

namespace DanBackup.Tests;

public sealed class CryptoTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("danbackup-crypto-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Encrypt_and_decrypt_round_trip()
    {
        var (crypto, _) = BackupCrypto.Create("senha-de-teste");
        var plain = Encoding.UTF8.GetBytes("ssid=Casa; senha=segredo123");
        var encrypted = crypto.Encrypt(plain);

        Assert.DoesNotContain("segredo123", Encoding.UTF8.GetString(encrypted));
        Assert.Equal(plain, crypto.Decrypt(encrypted));
    }

    [Fact]
    public void Open_with_correct_password_decrypts_and_wrong_password_is_rejected()
    {
        var (crypto, info) = BackupCrypto.Create("senha-certa");
        var encrypted = crypto.Encrypt("dados"u8);

        var reopened = BackupCrypto.Open(info, "senha-certa");
        Assert.Equal("dados"u8.ToArray(), reopened.Decrypt(encrypted));

        Assert.Throws<InvalidPasswordException>(() => BackupCrypto.Open(info, "senha-errada"));
    }

    [Fact]
    public void Tampered_data_is_detected()
    {
        var (crypto, _) = BackupCrypto.Create("senha-de-teste");
        var encrypted = crypto.Encrypt("dados importantes"u8);
        encrypted[^1] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => crypto.Decrypt(encrypted));
    }

    [Fact]
    public async Task Sensitive_path_set_is_stored_encrypted_and_restored_in_plain_text()
    {
        var (crypto, info) = BackupCrypto.Create("senha-de-teste");
        var source = Path.Combine(_temp, "origem", ".ssh");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "id_ed25519"), "-----BEGIN OPENSSH PRIVATE KEY-----");

        var itemDir = Path.Combine(_temp, "backup", "ssh");
        var ctx = new OperationContext { ModuleDir = itemDir, Settings = new AppSettings(), Reporter = NullReporter.Instance, Crypto = crypto };
        await PathSetCopier.BackupAsync([source], [], itemDir, CopyOptions.Everything, ctx, default, sensitive: true);

        var stored = Directory.GetFiles(itemDir, "*", SearchOption.AllDirectories);
        var single = Assert.Single(stored);
        Assert.EndsWith(BackupCrypto.EncryptedExtension, single);
        Assert.DoesNotContain("PRIVATE KEY", File.ReadAllText(single));

        // Restaura numa "máquina nova": senha digitada de novo.
        Directory.Delete(Path.Combine(_temp, "origem"), recursive: true);
        var restoreCtx = new OperationContext
        {
            ModuleDir = itemDir, Settings = new AppSettings(), Reporter = NullReporter.Instance,
            Crypto = BackupCrypto.Open(info, "senha-de-teste"),
        };
        await PathSetCopier.RestoreAsync(itemDir, restoreCtx, default);
        Assert.Equal("-----BEGIN OPENSSH PRIVATE KEY-----", File.ReadAllText(Path.Combine(source, "id_ed25519")));
    }

    [Fact]
    public async Task Sensitive_item_without_password_fails_instead_of_saving_plain_text()
    {
        var source = Path.Combine(_temp, "token.txt");
        File.WriteAllText(source, "token");
        var itemDir = Path.Combine(_temp, "backup");
        var ctx = new OperationContext { ModuleDir = itemDir, Settings = new AppSettings(), Reporter = NullReporter.Instance };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PathSetCopier.BackupAsync([source], [], itemDir, CopyOptions.Everything, ctx, default, sensitive: true));
        Assert.False(Directory.Exists(itemDir) && Directory.EnumerateFiles(itemDir, "*", SearchOption.AllDirectories).Any());
    }
}

public sealed class SecretStorageTests
{
    [Fact]
    public void Pfx_exported_with_backup_password_only_opens_with_it()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=DanBackup Teste", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddDays(1));

        var pfx = cert.ExportPkcs12(Pkcs12ExportPbeParameters.Pbes2Aes256Sha256, "senha-de-teste");

        using var loaded = X509CertificateLoader.LoadPkcs12(pfx, "senha-de-teste");
        Assert.True(loaded.HasPrivateKey);
        Assert.ThrowsAny<CryptographicException>(() =>
            X509CertificateLoader.LoadPkcs12(pfx, "errada"));
    }

    [Fact]
    public void Remembered_password_is_not_stored_in_plain_text()
    {
        var settings = new AppSettings();
        PasswordStore.Remember(settings, "senha-de-teste");

        Assert.NotNull(settings.ProtectedPassword);
        Assert.DoesNotContain("senha-de-teste", settings.ProtectedPassword);
        Assert.Equal("senha-de-teste", PasswordStore.Load(settings));

        PasswordStore.Forget(settings);
        Assert.Null(PasswordStore.Load(settings));
    }
}
