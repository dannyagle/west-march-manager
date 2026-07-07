namespace WestMarch.Domain.Items;

public enum ListingStatus
{
    Active = 0,
    Sold = 1,
    Cancelled = 2,
}

/// <summary>
/// A fixed-price marketplace listing. Bidding is deliberately deferred: if it arrives
/// later it attaches here as a Bids collection without reshaping this aggregate.
/// </summary>
public class MarketListing
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ItemInstanceId { get; set; }
    public ItemInstance ItemInstance { get; set; } = default!;

    public Guid SellerCharacterId { get; set; }

    public int AskingPriceGp { get; set; }

    public ListingStatus Status { get; set; } = ListingStatus.Active;

    /// <summary>Optimistic-concurrency token so two buyers cannot purchase the same listing.</summary>
    public int Version { get; set; }

    public DateTimeOffset ListedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }

    public Guid? BuyerCharacterId { get; set; }
}
