using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DanBackup.Core.Update;

/// <summary>Uma versão mais nova publicada no GitHub Releases.</summary>
public sealed record UpdateInfo(Version Version, string DownloadUrl, string? ChecksumUrl, string PageUrl);

/// <summary>
/// Atualização automática via GitHub Releases. O .exe em execução não pode ser sobrescrito, mas pode ser
/// renomeado: o novo é baixado ao lado, o atual vira DanBackup.exe.old e o novo assume o nome.
/// </summary>
public sealed class UpdateService
{
    public const string Repository = "damanfra/DanBackup";
    public const string AssetName = "DanBackup.exe";
    public const string OldSuffix = ".old";

    private readonly HttpClient _http;

    public UpdateService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DanBackup", "1.0"));
    }

    /// <summary>Versão do programa em execução (Major.Minor.Build).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = (System.Reflection.Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly).GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>Interpreta tags como "v0.3.12" (ou "0.3.12"); null se não for uma versão.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var text = tag.Trim().TrimStart('v', 'V');
        var dash = text.IndexOfAny(['-', '+']);
        if (dash >= 0) text = text[..dash];
        if (!Version.TryParse(text, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    /// <summary>Lê o JSON de "releases/latest" e devolve a atualização se for mais nova que <paramref name="current"/>.</summary>
    public static UpdateInfo? ParseRelease(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

        var version = ParseTag(root.GetProperty("tag_name").GetString());
        if (version is null || version <= current) return null;

        string? exe = null, sha = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase)) exe = url;
            else if (string.Equals(name, AssetName + ".sha256", StringComparison.OrdinalIgnoreCase)) sha = url;
        }
        if (exe is null) return null;

        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        return new UpdateInfo(version, exe, sha, page);
    }

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        return ParseRelease(json, CurrentVersion);
    }

    /// <summary>
    /// Baixa a versão nova, confere o SHA-256 (quando publicado) e a coloca no lugar do .exe atual.
    /// Devolve o caminho do executável pronto para ser iniciado.
    /// </summary>
    public async Task<string> InstallAsync(UpdateInfo update, string exePath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var newPath = exePath + ".new";
        var oldPath = exePath + OldSuffix;
        try
        {
            using (var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(newPath);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total.Value);
                }
            }

            if (update.ChecksumUrl is not null)
            {
                var expected = ParseChecksum(await _http.GetStringAsync(update.ChecksumUrl, ct));
                string actual;
                await using (var fs = File.OpenRead(newPath))
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("O arquivo baixado não confere com o SHA-256 publicado. A atualização foi cancelada.");
            }

            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(exePath, oldPath);
            try
            {
                File.Move(newPath, exePath);
            }
            catch
            {
                File.Move(oldPath, exePath); // desfaz
                throw;
            }
            return exePath;
        }
        finally
        {
            try { if (File.Exists(newPath)) File.Delete(newPath); } catch (IOException) { }
        }
    }

    /// <summary>Aceita "hash" ou "hash  arquivo" (formato do sha256sum).</summary>
    public static string ParseChecksum(string text) =>
        text.Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    /// <summary>Remove o .old deixado por uma atualização anterior.</summary>
    public static void CleanupOldVersion(string exePath)
    {
        try { File.Delete(exePath + OldSuffix); } catch (Exception) { /* ainda em uso: tenta na próxima vez */ }
    }
}
