using Microsoft.EntityFrameworkCore;
using WestMarch.Application.Characters;
using WestMarch.Domain.Characters;

namespace WestMarch.Infrastructure.Persistence.Repositories;

public class CharacterRepository(AppDbContext db) : ICharacterRepository
{
    public Task<Character?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Characters.FirstOrDefaultAsync(c => c.Id == id, ct);

    // DateTimeOffset ordering happens in memory: SQLite (used in tests, and a legal
    // provider swap) cannot ORDER BY DateTimeOffset, and these collections are small.
    public async Task<Character?> GetWithHistoryAsync(Guid id, CancellationToken ct = default)
    {
        var character = await db.Characters
            .AsNoTracking()
            .Include(c => c.Credits)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        character?.Credits.Sort((a, b) => b.AwardedAt.CompareTo(a.AwardedAt));
        return character;
    }

    public async Task<IReadOnlyList<Character>> ListByOwnerAsync(string ownerUserId, bool includeRetired, CancellationToken ct = default) =>
        await db.Characters
            .AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId && (includeRetired || !c.IsRetired))
            .OrderByDescending(c => c.Level).ThenBy(c => c.Name)
            .ToListAsync(ct);

    public void Add(Character character) => db.Characters.Add(character);

    public void AddCredit(SessionCredit credit) => db.SessionCredits.Add(credit);
}
