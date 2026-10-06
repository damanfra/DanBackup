using System.Security.Cryptography;
using System.Text;

namespace DanBackup.Core.Security;

/// <summary>Parâmetros de criptografia gravados no manifesto (nunca a senha nem a chave).</summary>
public sealed class EncryptionInfo
{
    public string Algorithm { get; set; } = "AES-256-GCM";
    public string Kdf { get; set; } = "PBKDF2-SHA256";
    public int Iterations { get; set; }
    public string Salt { get; set; } = "";

    /// <summary>Texto conhecido criptografado, para validar a senha antes de restaurar.</summary>
    public string Check { get; set; } = "";
}

public sealed class InvalidPasswordException() : Exception("Senha incorreta para este backup.");

/// <summary>
/// Criptografa os arquivos sensíveis do backup com AES-256-GCM.
/// A chave é derivada uma vez por backup (PBKDF2-SHA256 + salt aleatório).
/// Formato de cada arquivo: "DBE1" | nonce (12) | tag (16) | dados.
/// </summary>
public sealed class BackupCrypto
{
    public const string EncryptedExtension = ".dbenc";

    private const int DefaultIterations = 600_000;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string CheckText = "DanBackup";
    private static readonly byte[] Magic = "DBE1"u8.ToArray();

    private readonly byte[] _key;

    private BackupCrypto(byte[] key, string password)
    {
        _key = key;
        Password = password;
    }

    /// <summary>A senha em si — usada onde o formato tem proteção própria (ex.: .pfx).</summary>
    public string Password { get; }

    public static (BackupCrypto Crypto, EncryptionInfo Info) Create(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("A senha de criptografia não pode ser vazia.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(16);
        var crypto = new BackupCrypto(DeriveKey(password, salt, DefaultIterations), password);
        var info = new EncryptionInfo
        {
            Iterations = DefaultIterations,
            Salt = Convert.ToBase64String(salt),
            Check = Convert.ToBase64String(crypto.Encrypt(Encoding.UTF8.GetBytes(CheckText))),
        };
        return (crypto, info);
    }

    /// <summary>Abre um backup existente. Lança <see cref="InvalidPasswordException"/> se a senha estiver errada.</summary>
    public static BackupCrypto Open(EncryptionInfo info, string password)
    {
        var crypto = new BackupCrypto(DeriveKey(password, Convert.FromBase64String(info.Salt), info.Iterations), password);
        try
        {
            if (Encoding.UTF8.GetString(crypto.Decrypt(Convert.FromBase64String(info.Check))) == CheckText)
                return crypto;
        }
        catch (CryptographicException)
        {
        }
        throw new InvalidPasswordException();
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    public byte[] Encrypt(ReadOnlySpan<byte> plain)
    {
        var output = new byte[Magic.Length + NonceSize + TagSize + plain.Length];
        Magic.CopyTo(output, 0);
        var nonce = output.AsSpan(Magic.Length, NonceSize);
        var tag = output.AsSpan(Magic.Length + NonceSize, TagSize);
        var cipher = output.AsSpan(Magic.Length + NonceSize + TagSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return output;
    }

    /// <summary>Lança <see cref="CryptographicException"/> se a chave estiver errada ou o arquivo tiver sido alterado.</summary>
    public byte[] Decrypt(ReadOnlySpan<byte> data)
    {
        if (data.Length < Magic.Length + NonceSize + TagSize || !data[..Magic.Length].SequenceEqual(Magic))
            throw new CryptographicException("Arquivo não está no formato criptografado do DanBackup.");
        var nonce = data.Slice(Magic.Length, NonceSize);
        var tag = data.Slice(Magic.Length + NonceSize, TagSize);
        var cipher = data[(Magic.Length + NonceSize + TagSize)..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    public void EncryptFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, Encrypt(ReadAllBytesShared(source)));
    }

    public void DecryptFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, Decrypt(File.ReadAllBytes(source)));
    }

    /// <summary>Lê um arquivo mesmo que outro programa o esteja usando.</summary>
    public static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
