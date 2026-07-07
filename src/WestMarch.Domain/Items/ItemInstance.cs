namespace WestMarch.Domain.Items;

public enum InstanceStatus
{
    /// <summary>In a character's inventory.</summary>
    Owned = 0,

    /// <summary>Up for sale on the marketplace (still owned until bought).</summary>
    Listed = 1,

    /// <summary>Quick-sold to "the caravan" for half value; gone forever.</summary>
    QuickSold = 2,
}

/// <summary>
/// A concrete copy of a catalog item owned by a character — minted when a reward
/// is claimed, transferred when a marketplace sale completes. The ledger records
/// every acquisition and disposal.
/// </summary>
public class ItemInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CatalogItemId { get; set; }
    public CatalogItem CatalogItem { get; set; } = default!;

    /// <summary>Null after a quick-sell (the instance is retired, kept for history).</summary>
    public Guid? OwnerCharacterId { get; set; }

    public InstanceStatus Status { get; set; } = InstanceStatus.Owned;

    public DateTimeOffset AcquiredAt { get; set; } = DateTimeOffset.UtcNow;
}
