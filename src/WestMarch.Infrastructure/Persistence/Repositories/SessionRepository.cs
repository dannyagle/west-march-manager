using Microsoft.EntityFrameworkCore;
using WestMarch.Application.Sessions;
using WestMarch.Domain.Sessions;

namespace WestMarch.Infrastructure.Persistence.Repositories;

public class SessionRepository(AppDbContext db) : ISessionRepository
{
    // Note: all DateTimeOffset ordering happens in memory. SQLite (used by the test
    // suite, and a legal provider swap) cannot ORDER BY DateTimeOffset, and every
    // result set here is small (one session, or a window of scheduled sessions).

    public Task<GameSession?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Sessions
            .Include(s => s.Adventure)
            .Include(s => s.Signups).ThenInclude(su => su.Character)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<GameSession?> GetReadOnlyAsync(Guid id, CancellationToken ct = default)
    {
        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Adventure).ThenInclude(a => a.Tags)
            .Include(s => s.Adventure).ThenInclude(a => a.GuaranteedRewards.OrderBy(r => r.SortOrder))
            .Include(s => s.Adventure).ThenInclude(a => a.RewardOptionSets.OrderBy(o => o.SortOrder))
                .ThenInclude(o => o.Options.OrderBy(x => x.SortOrder))
                .ThenInclude(o => o.CatalogItem)
            .Include(s => s.Signups).ThenInclude(su => su.Character)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        session?.Signups.Sort((a, b) => a.SignedUpAt.CompareTo(b.SignedUpAt));
        return session;
    }

    public async Task<IReadOnlyList<GameSession>> ListScheduledAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default)
    {
        var list = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Adventure).ThenInclude(a => a.Tags)
            .Include(s => s.Signups).ThenInclude(su => su.Character)
            .Where(s => s.ScheduledAt >= from && s.ScheduledAt <= until && s.Status != SessionStatus.Cancelled)
            .ToListAsync(ct);

        return [.. list.OrderBy(s => s.ScheduledAt)];
    }

    public async Task<IReadOnlyList<GameSession>> ListNeedingDmAsync(DateTimeOffset from, CancellationToken ct = default)
    {
        var list = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Adventure).ThenInclude(a => a.Tags)
            .Include(s => s.Signups).ThenInclude(su => su.Character)
            .Where(s => s.Status == SessionStatus.Scheduled && s.DmUserId == null && s.ScheduledAt >= from)
            .ToListAsync(ct);

        return [.. list.OrderBy(s => s.ScheduledAt)];
    }

    public async Task<IReadOnlyList<GameSession>> ListByCharacterAsync(Guid characterId, CancellationToken ct = default)
    {
        var list = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Adventure)
            .Include(s => s.Signups)
            .Where(s => s.Status != SessionStatus.Cancelled
                && s.Signups.Any(su => su.CharacterId == characterId))
            .ToListAsync(ct);

        return [.. list.OrderByDescending(s => s.ScheduledAt)];
    }

    public async Task<IReadOnlyList<SessionMessage>> ListMessagesAsync(Guid sessionId, CancellationToken ct = default)
    {
        var list = await db.SessionMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .ToListAsync(ct);

        return [.. list.OrderBy(m => m.PostedAt)];
    }

    public void Add(GameSession session) => db.Sessions.Add(session);

    public void AddSignup(SessionSignup signup) => db.SessionSignups.Add(signup);

    public void AddMessage(SessionMessage message) => db.SessionMessages.Add(message);
}
