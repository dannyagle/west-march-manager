namespace WestMarch.Domain.Items;

public enum LedgerEntryType
{
    /// <summary>Gold granted by a session's guaranteed rewards.</summary>
    RewardGold = 0,

    /// <summary>Item granted by a session reward choice (or guaranteed item).</summary>
    RewardItem = 1,

    /// <summary>Free-text reward recorded for the sheet (no managed item created).</summary>
    RewardNote = 2,

    /// <summary>Instant sale for half value; item retired.</summary>
    QuickSell = 3,

    /// <summary>Proceeds from a marketplace listing that sold.</summary>
    SaleProceeds = 4,

    /// <summary>Gold spent buying a marketplace listing.</summary>
    Purchase = 5,
}

/// <summary>
/// Append-only transaction history: every gold movement and item acquisition/disposal,
/// per character. The character page shows their own slice; the CA audit page queries all.
/// </summary>
public class LedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CharacterId { get; set; }

    public LedgerEntryType Type { get; set; }

    /// <summary>Positive = gold in, negative = gold out, zero = item-only event.</summary>
    public int GoldDelta { get; set; }

    public Guid? ItemInstanceId { get; set; }

    /// <summary>Item name snapshot so history renders without joins and survives catalog edits.</summary>
    public string? ItemName { get; set; }

    /// <summary>The session that granted the reward, when applicable.</summary>
    public Guid? SessionId { get; set; }

    /// <summary>The marketplace listing involved, when applicable.</summary>
    public Guid? ListingId { get; set; }

    /// <summary>The other character in a marketplace trade, when applicable.</summary>
    public Guid? CounterpartyCharacterId { get; set; }

    /// <summary>Human-readable line, e.g. "Reward: The Goblin Watchtower — 150 gp Warden bounty".</summary>
    public string Description { get; set; } = default!;

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
