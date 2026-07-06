using Microsoft.EntityFrameworkCore;
using WestMarch.Application.Announcements;
using WestMarch.Domain.Announcements;

namespace WestMarch.Infrastructure.Persistence.Repositories;

public class AnnouncementRepository(AppDbContext db) : IAnnouncementRepository
{
    public Task<Announcement?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Announcements.FirstOrDefaultAsync(a => a.Id == id, ct);

    // Ordered in memory: SQLite cannot ORDER BY DateTimeOffset, and the set is small.
    public async Task<IReadOnlyList<Announcement>> ListActiveAsync(DateTimeOffset onDate, CancellationToken ct = default)
    {
        var list = await db.Announcements
            .AsNoTracking()
            .Where(a => a.ActiveFrom <= onDate && (a.ActiveUntil == null || a.ActiveUntil >= onDate))
            .ToListAsync(ct);

        return [.. list
            .OrderBy(a => a.SortOrder is null)
            .ThenBy(a => a.SortOrder)
            .ThenByDescending(a => a.ActiveFrom)];
    }

    public async Task<IReadOnlyList<Announcement>> ListAllAsync(CancellationToken ct = default)
    {
        var list = await db.Announcements.AsNoTracking().ToListAsync(ct);
        return [.. list.OrderByDescending(a => a.ActiveFrom)];
    }

    public void Add(Announcement announcement) => db.Announcements.Add(announcement);

    public void Remove(Announcement announcement) => db.Announcements.Remove(announcement);
}
