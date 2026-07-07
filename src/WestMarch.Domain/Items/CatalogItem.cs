namespace WestMarch.Domain.Items;

public enum CatalogSource
{
    /// <summary>Came from an uploaded reference file; base fields are overwritten on re-import.</summary>
    Imported = 0,

    /// <summary>CA-authored; never touched by imports.</summary>
    Custom = 1,
}

/// <summary>
/// One entry in the campaign item catalog — magic or mundane, imported or custom.
/// The uploaded file is only a feed: after import this row is the source of truth.
/// CA-set fields (<see cref="CampaignPriceGp"/>, <see cref="IsActive"/>) live in their
/// own columns and survive re-imports; items missing from a newer file are deactivated,
/// never deleted, because owned instances and ledger history reference them.
/// </summary>
public class CatalogItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ItemKind Kind { get; set; }

    public string Name { get; set; } = default!;

    public ItemRarity Rarity { get; set; }

    /// <summary>Source category, e.g. "Wondrous Item", "Weapon", "Adventuring Gear".</summary>
    public string Category { get; set; } = "";

    public bool RequiresAttunement { get; set; }

    /// <summary>Numeric price from the source file; null when the source listed Varies/Priceless/Special.</summary>
    public int? BasePriceGp { get; set; }

    /// <summary>Price exactly as printed in the source, e.g. "B+100 GP", "5 SP", "Varies".</summary>
    public string? PriceRaw { get; set; }

    /// <summary>True when the source price means "base mundane item cost + N gp".</summary>
    public bool PriceIsBasePlus { get; set; }

    /// <summary>
    /// CA price override. Wins over <see cref="BasePriceGp"/> everywhere, and is the only
    /// way to make Varies/Priceless items sellable. Never touched by imports.
    /// </summary>
    public int? CampaignPriceGp { get; set; }

    public string? ExternalUrl { get; set; }

    /// <summary>Extra source columns kept for display (damage, AC, weight, …) as JSON.</summary>
    public string? DetailsJson { get; set; }

    public CatalogSource Source { get; set; }

    /// <summary>Stable identity across imports: normalized name + rarity. Null for custom items.</summary>
    public string? ImportKey { get; set; }

    public Guid? LastImportBatchId { get; set; }

    /// <summary>False when a re-import no longer contains the item, or a CA retires it.</summary>
    public bool IsActive { get; set; } = true;

    public string? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The price the campaign actually uses.</summary>
    public int? EffectivePriceGp => CampaignPriceGp ?? BasePriceGp;

    /// <summary>Only priced items can be quick-sold or listed on the marketplace.</summary>
    public bool IsSellable => EffectivePriceGp is not null;

    /// <summary>Stable import key: lower-cased trimmed name + rarity.</summary>
    public static string MakeImportKey(string name, ItemRarity rarity) =>
        $"{name.Trim().ToLowerInvariant()}|{rarity}";
}
