using WestMarch.Domain.Bestiary;
using WestMarch.Domain.Items;

namespace WestMarch.Application.Bestiary;

public record MonsterFilter(
    string? Search = null,
    decimal? MaxCr = null,
    string? CreatureType = null,
    bool IncludeInactive = false);

public interface IMonsterRepository
{
    Task<Monster?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Untracked, filtered, ordered by CR then name.</summary>
    Task<IReadOnlyList<Monster>> ListAsync(MonsterFilter filter, CancellationToken ct = default);

    Task<IReadOnlyList<Monster>> ListByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>All monsters keyed by ImportKey, tracked — the working set for a re-import.</summary>
    Task<Dictionary<string, Monster>> GetByKeyAsync(CancellationToken ct = default);

    void Add(Monster monster);

    /// <summary>Monster imports are recorded in the same batch ledger as item imports.</summary>
    void AddBatch(ImportBatch batch);
}
