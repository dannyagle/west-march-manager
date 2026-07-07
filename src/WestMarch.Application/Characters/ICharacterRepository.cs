using WestMarch.Domain.Characters;

namespace WestMarch.Application.Characters;

public interface ICharacterRepository
{
    /// <summary>Tracked load for mutation.</summary>
    Task<Character?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Untracked read including credit history.</summary>
    Task<Character?> GetWithHistoryAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Character>> ListByOwnerAsync(string ownerUserId, bool includeRetired, CancellationToken ct = default);

    /// <summary>Batch name lookup for ledgers and audit rows.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> characterIds, CancellationToken ct = default);

    void Add(Character character);

    void AddCredit(SessionCredit credit);
}
