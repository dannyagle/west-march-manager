using WestMarch.Application.Characters;
using WestMarch.Application.Common;
using WestMarch.Application.Sessions;
using WestMarch.Domain.Characters;
using WestMarch.Domain.Items;

namespace WestMarch.Application.Items;

/// <summary>A browsable listing with its seller's character name resolved for display.</summary>
public record ListingView(MarketListing Listing, string SellerName);

public interface IMarketplaceService
{
    Task<IReadOnlyList<ListingView>> BrowseAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MarketListing>> MyListingsAsync(CancellationToken ct = default);

    /// <summary>Sell instantly to "the caravan" for half value. The instance is retired. Returns gold received.</summary>
    Task<int> QuickSellAsync(Guid instanceId, CancellationToken ct = default);

    Task<MarketListing> ListForSaleAsync(Guid instanceId, int askingPriceGp, CancellationToken ct = default);
    Task CancelListingAsync(Guid listingId, CancellationToken ct = default);

    /// <summary>Soft warnings (e.g. out-of-tier) before a purchase — flagged, never blocked.</summary>
    Task<IReadOnlyList<SignupWarning>> GetPurchaseWarningsAsync(Guid listingId, Guid buyerCharacterId, CancellationToken ct = default);

    Task BuyAsync(Guid listingId, Guid buyerCharacterId, CancellationToken ct = default);
}

/// <summary>
/// Player-to-player economy: quick-sell for half value, or fixed-price listings other
/// players buy outright for one of their characters. All movements hit the ledger.
/// </summary>
public class MarketplaceService(
    IMarketRepository market,
    IInventoryRepository inventory,
    ICharacterRepository characters,
    IUnitOfWork uow,
    ICurrentUser currentUser) : IMarketplaceService
{
    public async Task<IReadOnlyList<ListingView>> BrowseAsync(CancellationToken ct = default)
    {
        currentUser.RequireUserId();
        var listings = await market.ListActiveAsync(ct);
        var names = await characters.GetNamesAsync(listings.Select(l => l.SellerCharacterId), ct);

        return [.. listings.Select(l => new ListingView(
            l, names.GetValueOrDefault(l.SellerCharacterId, "a mysterious stranger")))];
    }

    public Task<IReadOnlyList<MarketListing>> MyListingsAsync(CancellationToken ct = default) =>
        market.ListBySellerUserAsync(currentUser.RequireUserId(), ct);

    public async Task<int> QuickSellAsync(Guid instanceId, CancellationToken ct = default)
    {
        var (instance, owner) = await GetOwnedInstanceAsync(instanceId, ct);

        var price = instance.CatalogItem.EffectivePriceGp
            ?? throw new AppValidationException(
                $"{instance.CatalogItem.Name} has no campaign price yet — ask a Campaign Admin to set one before selling.");

        var proceeds = price <= 1 ? price : price / 2;

        instance.Status = InstanceStatus.QuickSold;
        instance.OwnerCharacterId = null;
        owner.GoldGp += proceeds;

        inventory.AddLedger(new LedgerEntry
        {
            CharacterId = owner.Id,
            Type = LedgerEntryType.QuickSell,
            GoldDelta = proceeds,
            ItemInstanceId = instance.Id,
            ItemName = instance.CatalogItem.Name,
            Description = $"Quick-sold {instance.CatalogItem.Name} to the caravan for {proceeds} gp (half of {price} gp).",
        });

        await uow.SaveChangesAsync(ct);
        return proceeds;
    }

    public async Task<MarketListing> ListForSaleAsync(Guid instanceId, int askingPriceGp, CancellationToken ct = default)
    {
        var (instance, owner) = await GetOwnedInstanceAsync(instanceId, ct);

        if (!instance.CatalogItem.IsSellable)
        {
            throw new AppValidationException(
                $"{instance.CatalogItem.Name} has no campaign price yet — ask a Campaign Admin to set one before selling.");
        }

        if (askingPriceGp < 1)
        {
            throw new AppValidationException("The asking price must be at least 1 gp.");
        }

        var listing = new MarketListing
        {
            ItemInstanceId = instance.Id,
            SellerCharacterId = owner.Id,
            AskingPriceGp = askingPriceGp,
        };

        instance.Status = InstanceStatus.Listed;
        market.Add(listing);
        await uow.SaveChangesAsync(ct);
        return listing;
    }

    public async Task CancelListingAsync(Guid listingId, CancellationToken ct = default)
    {
        var listing = await market.GetAsync(listingId, ct)
            ?? throw new NotFoundException(nameof(MarketListing), listingId);

        var seller = await characters.GetAsync(listing.SellerCharacterId, ct)
            ?? throw new NotFoundException(nameof(Character), listing.SellerCharacterId);

        if (seller.OwnerUserId != currentUser.RequireUserId() && !currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Only the seller or a Campaign Admin can cancel a listing.");
        }

        if (listing.Status != ListingStatus.Active)
        {
            throw new AppValidationException("This listing is no longer active.");
        }

        listing.Status = ListingStatus.Cancelled;
        listing.ResolvedAt = DateTimeOffset.UtcNow;
        listing.Version++;
        listing.ItemInstance.Status = InstanceStatus.Owned;

        await uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SignupWarning>> GetPurchaseWarningsAsync(Guid listingId, Guid buyerCharacterId, CancellationToken ct = default)
    {
        var listing = await market.GetAsync(listingId, ct)
            ?? throw new NotFoundException(nameof(MarketListing), listingId);
        var buyer = await characters.GetAsync(buyerCharacterId, ct)
            ?? throw new NotFoundException(nameof(Character), buyerCharacterId);

        var warnings = new List<SignupWarning>();
        var item = listing.ItemInstance.CatalogItem;

        if (!RarityTiers.IsAppropriateFor(item.Rarity, buyer.Level))
        {
            var (min, max) = RarityTiers.LevelRange(item.Rarity);
            warnings.Add(new SignupWarning(
                $"{item.Name} is {item.Rarity.Display()} — usually for levels {min}–{max}; {buyer.Name} is level {buyer.Level}."));
        }

        return warnings;
    }

    public async Task BuyAsync(Guid listingId, Guid buyerCharacterId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();

        var listing = await market.GetAsync(listingId, ct)
            ?? throw new NotFoundException(nameof(MarketListing), listingId);

        if (listing.Status != ListingStatus.Active)
        {
            throw new AppValidationException("This listing is no longer available.");
        }

        var buyer = await characters.GetAsync(buyerCharacterId, ct)
            ?? throw new NotFoundException(nameof(Character), buyerCharacterId);

        if (buyer.OwnerUserId != userId)
        {
            throw new ForbiddenAccessException("You can only buy items for your own characters.");
        }

        if (buyer.IsRetired)
        {
            throw new AppValidationException($"{buyer.Name} is retired.");
        }

        if (buyer.Id == listing.SellerCharacterId)
        {
            throw new AppValidationException("A character cannot buy their own listing.");
        }

        if (buyer.GoldGp < listing.AskingPriceGp)
        {
            throw new AppValidationException(
                $"{buyer.Name} has {buyer.GoldGp} gp — not enough for the {listing.AskingPriceGp} gp asking price.");
        }

        var seller = await characters.GetAsync(listing.SellerCharacterId, ct)
            ?? throw new NotFoundException(nameof(Character), listing.SellerCharacterId);

        var item = listing.ItemInstance;
        var itemName = item.CatalogItem.Name;

        buyer.GoldGp -= listing.AskingPriceGp;
        seller.GoldGp += listing.AskingPriceGp;

        item.OwnerCharacterId = buyer.Id;
        item.Status = InstanceStatus.Owned;
        item.AcquiredAt = DateTimeOffset.UtcNow;

        listing.Status = ListingStatus.Sold;
        listing.BuyerCharacterId = buyer.Id;
        listing.ResolvedAt = DateTimeOffset.UtcNow;
        listing.Version++; // concurrency token: the losing concurrent buyer fails to commit

        inventory.AddLedger(new LedgerEntry
        {
            CharacterId = buyer.Id,
            Type = LedgerEntryType.Purchase,
            GoldDelta = -listing.AskingPriceGp,
            ItemInstanceId = item.Id,
            ItemName = itemName,
            ListingId = listing.Id,
            CounterpartyCharacterId = seller.Id,
            Description = $"Bought {itemName} from {seller.Name} for {listing.AskingPriceGp} gp.",
        });

        inventory.AddLedger(new LedgerEntry
        {
            CharacterId = seller.Id,
            Type = LedgerEntryType.SaleProceeds,
            GoldDelta = listing.AskingPriceGp,
            ItemInstanceId = item.Id,
            ItemName = itemName,
            ListingId = listing.Id,
            CounterpartyCharacterId = buyer.Id,
            Description = $"Sold {itemName} to {buyer.Name} for {listing.AskingPriceGp} gp.",
        });

        if (!await uow.TrySaveChangesAsync(ct))
        {
            throw new AppValidationException("Too slow — someone else just bought this item.");
        }
    }

    private async Task<(ItemInstance Instance, Character Owner)> GetOwnedInstanceAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await inventory.GetInstanceAsync(instanceId, ct)
            ?? throw new NotFoundException(nameof(ItemInstance), instanceId);

        if (instance.OwnerCharacterId is null || instance.Status != InstanceStatus.Owned)
        {
            throw new AppValidationException(instance.Status == InstanceStatus.Listed
                ? "This item is currently listed for sale — cancel the listing first."
                : "This item is no longer in an inventory.");
        }

        var owner = await characters.GetAsync(instance.OwnerCharacterId.Value, ct)
            ?? throw new NotFoundException(nameof(Character), instance.OwnerCharacterId.Value);

        if (owner.OwnerUserId != currentUser.RequireUserId())
        {
            throw new ForbiddenAccessException("You can only sell items belonging to your own characters.");
        }

        return (instance, owner);
    }
}
