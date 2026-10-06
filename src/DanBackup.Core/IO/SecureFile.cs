using DanBackup.Core.Engine;
using DanBackup.Core.Security;

namespace DanBackup.Core.IO;

public static class SecureFile
{
    /// <summary>Lê um arquivo do backup, descriptografando se ele tiver sido salvo como .dbenc. Null se não existir.</summary>
    public static byte[]? ReadAllBytes(string pathWithoutEncryptedSuffix, OperationContext ctx)
    {
        var encrypted = pathWithoutEncryptedSuffix + BackupCrypto.EncryptedExtension;
        if (File.Exists(encrypted)) return ctx.RequireCrypto().Decrypt(File.ReadAllBytes(encrypted));
        return File.Exists(pathWithoutEncryptedSuffix) ? File.ReadAllBytes(pathWithoutEncryptedSuffix) : null;
    }

    /// <summary>Um .reg exportado pelo reg.exe começa com "Windows Registry Editor" (em UTF-16).</summary>
    public static bool IsValidRegFile(byte[] content)
    {
        using var reader = new StreamReader(new MemoryStream(content), detectEncodingFromByteOrderMarks: true);
        return reader.ReadLine()?.StartsWith("Windows Registry Editor", StringComparison.Ordinal) == true;
    }
}
