using Microsoft.EntityFrameworkCore;
using WestMarch.Application.Items;
using WestMarch.Domain.Items;
using WestMarch.Infrastructure.Persistence;

namespace WestMarch.Infrastructure.Items;

// DateTimeOffset ordering happens in memory throughout (SQLite compatibility; small sets).

public class CatalogRepository(AppDbContext db) : ICatalogRepository
{
    public Task<CatalogItem?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.CatalogItems.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<CatalogItem>> ListAsync(CatalogFilter filter, CancellationToken ct = default)
    {
        var query = db.CatalogItems.AsNoTracking();

        if (filter.Kind is not null)
        {
            query = query.Where(c => c.Kind == filter.Kind);
        }

        if (filter.Rarity is not null)
        {
            query = query.Where(c => c.Rarity == filter.Rarity);
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query = query.Where(c => c.Category == filter.Category);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            query = query.Where(c => c.Name.Contains(s));
        }

        if (filter.UnpricedOnly)
        {
            query = query.Where(c => c.BasePriceGp == null && c.CampaignPriceGp == null);
        }

        if (!filter.IncludeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query.OrderBy(c => c.Rarity).ThenBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CatalogItem>> ListByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var wanted = ids.Distinct().ToList();
        return wanted.Count == 0
            ? []
            : await db.CatalogItems.AsNoTracking().Where(c => wanted.Contains(c.Id)).ToListAsync(ct);
    }

    public async Task<Dictionary<string, CatalogItem>> GetImportedByKeyAsync(ItemKind kind, CancellationToken ct = default) =>
        await db.CatalogItems
            .Where(c => c.Source == CatalogSource.Imported && c.Kind == kind && c.ImportKey != null)
            .ToDictionaryAsync(c => c.ImportKey!, ct);

    public void Add(CatalogItem item) => db.CatalogItems.Add(item);

    public void AddBatch(ImportBatch batch) => db.ImportBatches.Add(batch);

    public async Task<IReadOnlyList<ImportBatch>> ListBatchesAsync(CancellationToken ct = default)
    {
        var list = await db.ImportBatches.AsNoTracking().ToListAsync(ct);
        return [.. list.OrderByDescending(b => b.UploadedAt)];
    }
}

public class InventoryRepository(AppDbContext db) : IInventoryRepository
{
    public Task<ItemInstance?> GetInstanceAsync(Guid id, CancellationToken ct = default) =>
        db.ItemInstances.Include(i => i.CatalogItem).FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<ItemInstance>> ListOwnedAsync(Guid characterId, CancellationToken ct = default)
    {
        var list = await db.ItemInstances
            .AsNoTracking()
            .Include(i => i.CatalogItem)
            .Where(i => i.OwnerCharacterId == characterId && i.Status != InstanceStatus.QuickSold)
            .ToListAsync(ct);

        return [.. list.OrderBy(i => i.CatalogItem.Rarity).ThenBy(i => i.CatalogItem.Name)];
    }

    public async Task<IReadOnlyList<LedgerEntry>> ListLedgerAsync(Guid characterId, CancellationToken ct = default)
    {
        var list = await db.LedgerEntries
            .AsNoTracking()
            .Where(l => l.CharacterId == characterId)
            .ToListAsync(ct);

        return [.. list.OrderByDescending(l => l.OccurredAt)];
    }

    public async Task<IReadOnlyList<LedgerEntry>> ListLedgerAllAsync(AuditFilter filter, CancellationToken ct = default)
    {
        var query = db.LedgerEntries.AsNoTracking();

        if (filter.CharacterId is not null)
        {
            query = query.Where(l => l.CharacterId == filter.CharacterId);
        }

        if (filter.Type is not null)
        {
            query = query.Where(l => l.Type == filter.Type);
        }

        if (filter.From is not null)
        {
            query = query.Where(l => l.OccurredAt >= filter.From);
        }

        if (filter.Until is not null)
        {
            query = query.Where(l => l.OccurredAt <= filter.Until);
        }

        var list = await query.ToListAsync(ct);
        return [.. list.OrderByDescending(l => l.OccurredAt).Take(filter.Take)];
    }

    public void AddInstance(ItemInstance instance) => db.ItemInstances.Add(instance);

    public void AddLedger(LedgerEntry entry) => db.LedgerEntries.Add(entry);
}

public class MarketRepository(AppDbContext db) : IMarketRepository
{
    public Task<MarketListing?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.MarketListings
            .Include(l => l.ItemInstance).ThenInclude(i => i.CatalogItem)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<IReadOnlyList<MarketListing>> ListActiveAsync(CancellationToken ct = default)
    {
        var list = await db.MarketListings
            .AsNoTracking()
            .Include(l => l.ItemInstance).ThenInclude(i => i.CatalogItem)
            .Where(l => l.Status == ListingStatus.Active)
            .ToListAsync(ct);

        return [.. list.OrderByDescending(l => l.ListedAt)];
    }

    public async Task<IReadOnlyList<MarketListing>> ListBySellerUserAsync(string userId, CancellationToken ct = default)
    {
        var list = await (
            from listing in db.MarketListings.AsNoTracking()
                .Include(l => l.ItemInstance).ThenInclude(i => i.CatalogItem)
            join character in db.Characters on listing.SellerCharacterId equals character.Id
            where character.OwnerUserId == userId
            select listing).ToListAsync(ct);

        return [.. list.OrderByDescending(l => l.ListedAt)];
    }

    public void Add(MarketListing listing) => db.MarketListings.Add(listing);
}
