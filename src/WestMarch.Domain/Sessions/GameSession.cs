using WestMarch.Domain.Adventures;

namespace WestMarch.Domain.Sessions;

public enum SessionStatus
{
    Scheduled = 0,
    Completed = 1,
    Cancelled = 2,
}

/// <summary>
/// A scheduled play session of an approved adventure. May be created by a player or a DM;
/// a session without a DM is a first-class state surfaced on the DM board.
/// </summary>
public class GameSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AdventureId { get; set; }
    public Adventure Adventure { get; set; } = default!;

    public DateTimeOffset ScheduledAt { get; set; }

    /// <summary>Null while the session still needs a DM.</summary>
    public string? DmUserId { get; set; }

    public string CreatedByUserId { get; set; } = default!;

    public SessionStatus Status { get; set; } = SessionStatus.Scheduled;

    /// <summary>Optional venue / logistics note, e.g. "Back table at The Dragon's Hoard".</summary>
    public string? Location { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<SessionSignup> Signups { get; set; } = [];
    public List<SessionMessage> Messages { get; set; } = [];

    // Extension point (deferred): reservable resources — e.g. a physical store table with
    // limited seating — will attach here as a SessionResourceAllocation collection referencing
    // a Resource aggregate. Reserved by design; no Phase 1 tables.

    public bool NeedsDm => DmUserId is null && Status == SessionStatus.Scheduled;

    public bool HasOpenSeats => Status == SessionStatus.Scheduled
        && Signups.Count < Adventure?.TargetPlayersMax;
}
