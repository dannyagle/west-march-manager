using Microsoft.EntityFrameworkCore;
using WestMarch.Application.Adventures;
using WestMarch.Domain.Adventures;

namespace WestMarch.Infrastructure.Persistence.Repositories;

public class AdventureRepository(AppDbContext db) : IAdventureRepository
{
    private static IQueryable<Adventure> WithStructure(IQueryable<Adventure> q) => q
        .Include(a => a.Tags)
        .Include(a => a.GuaranteedRewards.OrderBy(r => r.SortOrder))
        .Include(a => a.RewardOptionSets.OrderBy(s => s.SortOrder))
            .ThenInclude(s => s.Options.OrderBy(o => o.SortOrder));

    public Task<Adventure?> GetAsync(Guid id, CancellationToken ct = default) =>
        WithStructure(db.Adventures).FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Adventure?> GetReadOnlyAsync(Guid id, CancellationToken ct = default) =>
        WithStructure(db.Adventures).AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<Adventure>> ListAsync(CancellationToken ct = default)
    {
        // Ordered in memory: SQLite cannot ORDER BY DateTimeOffset.
        var list = await db.Adventures
            .AsNoTracking()
            .Include(a => a.Tags)
            .ToListAsync(ct);
        return [.. list.OrderByDescending(a => a.UpdatedAt)];
    }

    public async Task<IReadOnlyList<Adventure>> ListApprovedActiveAsync(DateTimeOffset onDate, CancellationToken ct = default) =>
        await db.Adventures
            .AsNoTracking()
            .Include(a => a.Tags)
            .Where(a => a.Status == AdventureStatus.Approved
                && a.ActiveFrom <= onDate
                && (a.ActiveUntil == null || a.ActiveUntil >= onDate))
            .OrderBy(a => a.MinLevel).ThenBy(a => a.Title)
            .ToListAsync(ct);

    public void Add(Adventure adventure) => db.Adventures.Add(adventure);

    public void AddRewardComponents(IEnumerable<RewardComponent> components) =>
        db.RewardComponents.AddRange(components);

    public void AddRewardOptionSets(IEnumerable<RewardOptionSet> sets) =>
        db.RewardOptionSets.AddRange(sets);

    public void RemoveRewardComponents(IEnumerable<RewardComponent> components) =>
        db.RewardComponents.RemoveRange(components);

    public void RemoveRewardOptionSets(IEnumerable<RewardOptionSet> sets) =>
        db.RewardOptionSets.RemoveRange(sets);
}

public class TagRepository(AppDbContext db) : ITagRepository
{
    public async Task<IReadOnlyList<Tag>> ListAsync(CancellationToken ct = default) =>
        await db.Tags.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);

    public async Task<List<Tag>> GetOrCreateAsync(IEnumerable<string> names, CancellationToken ct = default)
    {
        var wanted = names.ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var lowered = wanted.Select(n => n.ToLowerInvariant()).ToList();
        var existing = await db.Tags.Where(t => lowered.Contains(t.Name.ToLower())).ToListAsync(ct);

        var result = new List<Tag>(existing);
        foreach (var name in wanted)
        {
            if (!existing.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                var tag = new Tag { Name = name };
                db.Tags.Add(tag);
                result.Add(tag);
            }
        }

        return result;
    }
}
