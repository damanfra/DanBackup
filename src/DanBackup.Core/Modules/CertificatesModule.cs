using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DanBackup.Core.Engine;
using DanBackup.Core.Model;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

/// <summary>Certificados pessoais (Usuário atual → Pessoal), com chave privada quando exportável.</summary>
public sealed class CertificatesModule : BackupModuleBase
{
    public override string Id => "certificates";
    public override string Name => "Certificados pessoais";
    public override string Description => "Certificados do repositório Pessoal do usuário (ex.: e-CPF/e-CNPJ A1, certificados de cliente).";

    public override string? Notes =>
        "🔒 Os .pfx são protegidos com a senha do backup (dá para importá-los também pelo Windows com essa senha). " +
        "Certificados não exportáveis (ex.: token/cartão A3) só têm a parte pública salva.";

    public override Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        IReadOnlyList<BackupItem> items = store.Certificates
            // Certificados de ingresso do dispositivo em contas corporativas (Entra ID) não são do usuário.
            .Where(c => !c.Issuer.Contains("MS-Organization-", StringComparison.OrdinalIgnoreCase))
            .Select(c => new BackupItem
            {
                Id = c.Thumbprint,
                DisplayName = c.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
                Description = $"válido até {c.NotAfter:dd/MM/yyyy}" + (c.HasPrivateKey ? " · com chave privada" : "") +
                              $" · emissor: {c.GetNameInfo(X509NameType.SimpleName, forIssuer: true)}",
                SelectedByDefault = c.NotAfter > DateTime.Now,
                Data = { ["thumbprint"] = c.Thumbprint },
            })
            .ToList();
        return Task.FromResult(items);
    }

    public override Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var cert = store.Certificates.Find(X509FindType.FindByThumbprint, item.Get("thumbprint")!, validOnly: false)
                       .FirstOrDefault()
                   ?? throw new InvalidOperationException("Certificado não encontrado.");

        var dir = ctx.GetItemDir(item);
        Directory.CreateDirectory(dir);
        if (cert.HasPrivateKey)
        {
            try
            {
                // .pfx protegido com a senha do backup (AES-256), importável direto pelo Windows com essa senha.
                var pfx = cert.ExportPkcs12(Pkcs12ExportPbeParameters.Pbes2Aes256Sha256, ctx.RequireCrypto().Password);
                File.WriteAllBytes(Path.Combine(dir, "certificado.pfx"), pfx);
                return Task.CompletedTask;
            }
            catch (CryptographicException)
            {
                ctx.Warn($"{item.DisplayName}: chave privada não exportável; salvo só o certificado público.");
            }
        }
        File.WriteAllBytes(Path.Combine(dir, "certificado.cer"), cert.Export(X509ContentType.Cert));
        return Task.CompletedTask;
    }

    public override Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var dir = ctx.GetItemDir(item);
        var pfx = Path.Combine(dir, "certificado.pfx");
        using var cert = File.Exists(pfx)
            ? X509CertificateLoader.LoadPkcs12FromFile(pfx, ctx.Crypto?.Password,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable)
            : X509CertificateLoader.LoadCertificateFromFile(Path.Combine(dir, "certificado.cer"));

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(cert);
        return Task.CompletedTask;
    }

    /// <summary>Abre o .pfx com a senha (chave efêmera: nada é instalado) e confere a chave privada.</summary>
    public override Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var dir = ctx.GetItemDir(item);
        var pfx = Path.Combine(dir, "certificado.pfx");
        try
        {
            if (File.Exists(pfx))
            {
                using var cert = X509CertificateLoader.LoadPkcs12FromFile(pfx, ctx.Crypto?.Password, X509KeyStorageFlags.EphemeralKeySet);
                if (!cert.HasPrivateKey)
                    ctx.Warn("O .pfx abriu, mas sem a chave privada.");
                if (cert.NotAfter < DateTime.Now)
                    ctx.Warn($"Certificado vencido em {cert.NotAfter:dd/MM/yyyy}.");
            }
            else
            {
                using var cert = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(dir, "certificado.cer"));
                ctx.Warn("Só a parte pública foi salva (chave privada não exportável).");
            }
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException($"O certificado não abre com a senha do backup: {ex.Message}");
        }
        return Task.CompletedTask;
    }
}
