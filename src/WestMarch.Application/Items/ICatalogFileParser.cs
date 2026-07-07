using WestMarch.Domain.Items;

namespace WestMarch.Application.Items;

public record ParsedCatalogItem(
    string Name,
    ItemRarity Rarity,
    string Category,
    bool RequiresAttunement,
    int? BasePriceGp,
    string? PriceRaw,
    bool PriceIsBasePlus,
    string? ExternalUrl,
    string? DetailsJson)
{
    public string ImportKey => CatalogItem.MakeImportKey(Name, Rarity);
}

public record ParsedCatalogFile(
    ItemKind Kind,
    string? SourceNote,
    IReadOnlyList<ParsedCatalogItem> Items);

/// <summary>
/// Parses the campaign's reference files (magic-items.json / equipment.json shapes)
/// into a provider-neutral form. Implemented in Infrastructure.
/// </summary>
public interface ICatalogFileParser
{
    /// <summary>Throws AppValidationException when the payload doesn't match the expected shape.</summary>
    Task<ParsedCatalogFile> ParseAsync(Stream json, ItemKind kind, CancellationToken ct = default);
}
