using WestMarch.Domain.Characters;

namespace WestMarch.Domain.Sessions;

/// <summary>A character signed up for a session.</summary>
public class SessionSignup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }
    public GameSession Session { get; set; } = default!;

    public Guid CharacterId { get; set; }
    public Character Character { get; set; } = default!;

    public DateTimeOffset SignedUpAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set by the DM during completion: whether this character succeeded and earned a session credit.</summary>
    public bool? ReceivedCredit { get; set; }
}
