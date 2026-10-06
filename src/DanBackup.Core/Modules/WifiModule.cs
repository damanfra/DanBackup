using System.Xml.Linq;
using DanBackup.Core.Engine;
using DanBackup.Core.IO;
using DanBackup.Core.Model;
using DanBackup.Core.Platform;
using DanBackup.Core.Security;
using DanBackup.Core.Settings;

namespace DanBackup.Core.Modules;

/// <summary>Perfis de redes Wi-Fi com as senhas (netsh wlan export ... key=clear).</summary>
public sealed class WifiModule : BackupModuleBase
{
    public override string Id => "wifi";
    public override string Name => "Redes Wi-Fi";
    public override string Description => "Redes Wi-Fi salvas, com as senhas.";
    public override bool BackupRequiresAdmin => true;

    public override string? Notes => "🔒 Os perfis (com as senhas) são salvos criptografados com a senha do backup.";

    public override async Task<IReadOnlyList<BackupItem>> DiscoverAsync(AppSettings settings, CancellationToken ct)
    {
        var temp = Directory.CreateTempSubdirectory("danbackup-wifi-").FullName;
        try
        {
            var result = await ProcessRunner.RunAsync("netsh.exe", $"wlan export profile folder=\"{temp}\"", ct);
            if (result.ExitCode != 0) return []; // sem placa Wi-Fi ou serviço WLAN parado

            return Directory.EnumerateFiles(temp, "*.xml")
                .Select(ReadProfileName)
                .OfType<string>()
                .Distinct()
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(name => new BackupItem
                {
                    Id = PathUtil.SafeName(name),
                    DisplayName = name,
                    Data = { ["profile"] = name },
                })
                .ToList();
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    public override async Task BackupItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var crypto = ctx.RequireCrypto();
        var dir = ctx.GetItemDir(item);
        Directory.CreateDirectory(dir);

        // O XML exportado tem a senha em texto puro: fica só numa pasta temporária e é criptografado no backup.
        var temp = Directory.CreateTempSubdirectory("danbackup-wifi-").FullName;
        try
        {
            var result = await ProcessRunner.RunAsync("netsh.exe",
                $"wlan export profile name=\"{item.Get("profile")}\" key=clear folder=\"{temp}\"", ct);
            var files = Directory.GetFiles(temp, "*.xml");
            if (result.ExitCode != 0 || files.Length == 0)
                throw new InvalidOperationException($"netsh falhou: {result.LastLine}");
            foreach (var xml in files)
                crypto.EncryptFile(xml, Path.Combine(dir, Path.GetFileName(xml) + BackupCrypto.EncryptedExtension));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    public override async Task RestoreItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        var temp = Directory.CreateTempSubdirectory("danbackup-wifi-").FullName;
        try
        {
            foreach (var file in Directory.EnumerateFiles(ctx.GetItemDir(item)))
            {
                var xml = file;
                if (file.EndsWith(BackupCrypto.EncryptedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    xml = Path.Combine(temp, Path.GetFileNameWithoutExtension(file));
                    ctx.RequireCrypto().DecryptFile(file, xml);
                }
                else if (!file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var result = await ProcessRunner.RunAsync("netsh.exe", $"wlan add profile filename=\"{xml}\" user=all", ct);
                if (result.ExitCode != 0)
                    throw new InvalidOperationException($"netsh falhou: {result.LastLine}");
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>Cada perfil descriptografa, é um XML de perfil WLAN válido e contém a senha (se a rede não for aberta).</summary>
    public override Task VerifyItemAsync(BackupItem item, OperationContext ctx, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(ctx.GetItemDir(item)))
        {
            var bytes = SecureFile.ReadAllBytes(
                file.EndsWith(BackupCrypto.EncryptedExtension, StringComparison.OrdinalIgnoreCase) ? file[..^BackupCrypto.EncryptedExtension.Length] : file,
                ctx)!;
            XDocument doc;
            try
            {
                doc = XDocument.Load(new MemoryStream(bytes));
            }
            catch (System.Xml.XmlException ex)
            {
                throw new InvalidDataException($"Perfil Wi-Fi inválido ({Path.GetFileName(file)}): {ex.Message}");
            }

            var elements = doc.Descendants().ToList();
            if (!elements.Any(e => e.Name.LocalName == "name"))
                throw new InvalidDataException($"Perfil Wi-Fi sem nome: {Path.GetFileName(file)}");
            var auth = elements.FirstOrDefault(e => e.Name.LocalName == "authentication")?.Value;
            bool open = string.Equals(auth, "open", StringComparison.OrdinalIgnoreCase);
            if (!open && !elements.Any(e => e.Name.LocalName == "keyMaterial"))
                ctx.Warn("A senha da rede não está no backup (rede corporativa/802.1X ou backup feito sem administrador).");
        }
        return Task.CompletedTask;
    }

    private static string? ReadProfileName(string xmlFile)
    {
        try
        {
            var doc = XDocument.Load(xmlFile);
            return doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value;
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            return null;
        }
    }
}
