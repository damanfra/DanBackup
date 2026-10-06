using System.Text.Json;

namespace DanBackup.Core.Catalog;

/// <summary>
/// Um programa ou jogo conhecido, com os locais onde ele guarda configurações.
/// Os caminhos aceitam tokens (%APPDATA%, %DOCUMENTS%, %STEAMAPP:730%...) e curingas (*).
/// </summary>
public sealed class CatalogEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Notes { get; set; }

    /// <summary>Contém senhas/chaves/tokens: salvo criptografado.</summary>
    public bool Sensitive { get; set; }
    public List<string> Paths { get; set; } = [];
    public List<string> Registry { get; set; } = [];
}

public static class CatalogLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Carrega um catálogo embutido (apps.json, games.json).</summary>
    public static IReadOnlyList<CatalogEntry> LoadEmbedded(string fileName)
    {
        var assembly = typeof(CatalogLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream($"DanBackup.Catalogs.{fileName}")
                           ?? throw new FileNotFoundException($"Catálogo embutido não encontrado: {fileName}");
        return JsonSerializer.Deserialize<List<CatalogEntry>>(stream, Options) ?? [];
    }
}
