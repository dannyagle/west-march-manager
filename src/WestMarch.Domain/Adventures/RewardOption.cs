namespace WestMarch.Domain.Adventures;

using WestMarch.Domain.Items;

/// <summary>
/// One selectable option inside a "choose 1 of N" set. Either catalog-backed
/// (<see cref="CatalogItemId"/> set — claiming it mints a real inventory instance)
/// or free text (recorded on the character's ledger as a note for their sheet).
/// This was the seam reserved in Phase 1; the catalog now fills it.
/// </summary>
public class RewardOption
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RewardOptionSetId { get; set; }

    /// <summary>Free-text description; for catalog-backed options this is a display snapshot of the item name.</summary>
    public string Description { get; set; } = default!;

    /// <summary>Optional direct link; catalog-backed options usually rely on the catalog item's URL instead.</summary>
    public string? ExternalUrl { get; set; }

    /// <summary>Reference into the CA-managed item catalog; null for free-text options.</summary>
    public Guid? CatalogItemId { get; set; }
    public CatalogItem? CatalogItem { get; set; }

    public int SortOrder { get; set; }
}
