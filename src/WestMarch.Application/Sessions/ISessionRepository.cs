using WestMarch.Domain.Sessions;

namespace WestMarch.Application.Sessions;

public interface ISessionRepository
{
    /// <summary>Tracked load with adventure, signups + characters, for mutation.</summary>
    Task<GameSession?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Untracked read with adventure (incl. tags/rewards), signups + characters.</summary>
    Task<GameSession?> GetReadOnlyAsync(Guid id, CancellationToken ct = default);

    /// <summary>Untracked scheduled sessions in a window, with adventure and signups.</summary>
    Task<IReadOnlyList<GameSession>> ListScheduledAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default);

    /// <summary>Scheduled sessions with no assigned DM, soonest first.</summary>
    Task<IReadOnlyList<GameSession>> ListNeedingDmAsync(DateTimeOffset from, CancellationToken ct = default);

    Task<IReadOnlyList<SessionMessage>> ListMessagesAsync(Guid sessionId, CancellationToken ct = default);

    void Add(GameSession session);

    /// <summary>Explicitly tracks a new signup as Added. Required because our entities
    /// pre-generate Guid keys, so navigation-fixup would misread new children as Modified.</summary>
    void AddSignup(SessionSignup signup);

    void AddMessage(SessionMessage message);
}
