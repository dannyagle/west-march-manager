namespace WestMarch.Domain.Adventures;

/// <summary>
/// One selectable option inside a "choose 1 of N" set. Modeled as a real object,
/// not a bare string: today it is populated by free text, but it reserves the seam
/// for the future CA-managed catalog — when the catalog ships, options graduate by
/// setting <see cref="CatalogItemId"/> to reference a catalog entry (which will carry
/// its own external URL / sourcebook citation). No schema rework required.
/// </summary>
public class RewardOption
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RewardOptionSetId { get; set; }

    /// <summary>Free-text description of the option (Phase 1 source of truth).</summary>
    public string Description { get; set; } = default!;

    /// <summary>Optional direct link (D&D Beyond, dnd2024.wikidot.com, etc.) until the catalog exists.</summary>
    public string? ExternalUrl { get; set; }

    /// <summary>Reserved: future FK into the CA-managed item/reference catalog. Unused in Phase 1.</summary>
    public Guid? CatalogItemId { get; set; }

    public int SortOrder { get; set; }
}
