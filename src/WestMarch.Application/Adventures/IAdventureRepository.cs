using WestMarch.Domain.Adventures;

namespace WestMarch.Application.Adventures;

public interface IAdventureRepository
{
    /// <summary>Tracked load with tags and reward structure, for mutation.</summary>
    Task<Adventure?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Untracked read with tags and reward structure.</summary>
    Task<Adventure?> GetReadOnlyAsync(Guid id, CancellationToken ct = default);

    /// <summary>All adventures with tags, untracked. Visibility filtering happens in the service.</summary>
    Task<IReadOnlyList<Adventure>> ListAsync(CancellationToken ct = default);

    /// <summary>Approved adventures whose active window contains <paramref name="onDate"/>.</summary>
    Task<IReadOnlyList<Adventure>> ListApprovedActiveAsync(DateTimeOffset onDate, CancellationToken ct = default);

    void Add(Adventure adventure);

    /// <summary>Explicitly tracks new reward rows as Added. Required because our entities
    /// pre-generate Guid keys, so navigation-fixup would misread new children as Modified.</summary>
    void AddRewardComponents(IEnumerable<RewardComponent> components);
    void AddRewardOptionSets(IEnumerable<RewardOptionSet> sets);

    void RemoveRewardComponents(IEnumerable<RewardComponent> components);
    void RemoveRewardOptionSets(IEnumerable<RewardOptionSet> sets);
}

public interface ITagRepository
{
    Task<IReadOnlyList<Tag>> ListAsync(CancellationToken ct = default);

    /// <summary>Returns existing tags by name (case-insensitive), creating any that are missing.</summary>
    Task<List<Tag>> GetOrCreateAsync(IEnumerable<string> names, CancellationToken ct = default);
}
