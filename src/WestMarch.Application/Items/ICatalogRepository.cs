using WestMarch.Domain.Items;

namespace WestMarch.Application.Items;

public record CatalogFilter(
    ItemKind? Kind = null,
    ItemRarity? Rarity = null,
    string? Category = null,
    string? Search = null,
    bool UnpricedOnly = false,
    bool IncludeInactive = false);

public interface ICatalogRepository
{
    /// <summary>Tracked load for mutation.</summary>
    Task<CatalogItem?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Untracked filtered list for browsing.</summary>
    Task<IReadOnlyList<CatalogItem>> ListAsync(CatalogFilter filter, CancellationToken ct = default);

    /// <summary>Untracked lookup of several items at once (reward pickers, claim validation).</summary>
    Task<IReadOnlyList<CatalogItem>> ListByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>All imported items of a kind, tracked, keyed by ImportKey — the working set for a re-import.</summary>
    Task<Dictionary<string, CatalogItem>> GetImportedByKeyAsync(ItemKind kind, CancellationToken ct = default);

    void Add(CatalogItem item);

    void AddBatch(ImportBatch batch);

    Task<IReadOnlyList<ImportBatch>> ListBatchesAsync(CancellationToken ct = default);
}

public interface IInventoryRepository
{
    /// <summary>Tracked instance with its catalog item, for mutation.</summary>
    Task<ItemInstance?> GetInstanceAsync(Guid id, CancellationToken ct = default);

    /// <summary>Untracked inventory (Owned + Listed) with catalog items.</summary>
    Task<IReadOnlyList<ItemInstance>> ListOwnedAsync(Guid characterId, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerEntry>> ListLedgerAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>Campaign-wide ledger slice for the CA audit page, newest first.</summary>
    Task<IReadOnlyList<LedgerEntry>> ListLedgerAllAsync(AuditFilter filter, CancellationToken ct = default);

    void AddInstance(ItemInstance instance);

    void AddLedger(LedgerEntry entry);
}

public record AuditFilter(
    Guid? CharacterId = null,
    LedgerEntryType? Type = null,
    DateTimeOffset? From = null,
    DateTimeOffset? Until = null,
    int Take = 200);

public interface IMarketRepository
{
    /// <summary>Tracked listing with instance + catalog item, for mutation.</summary>
    Task<MarketListing?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Untracked active listings with instance + catalog item, newest first.</summary>
    Task<IReadOnlyList<MarketListing>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>Untracked listings (any status) whose seller belongs to the given user.</summary>
    Task<IReadOnlyList<MarketListing>> ListBySellerUserAsync(string userId, CancellationToken ct = default);

    void Add(MarketListing listing);
}
