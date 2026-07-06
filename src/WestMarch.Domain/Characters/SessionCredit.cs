namespace WestMarch.Domain.Characters;

/// <summary>
/// Audit record of one successful session credited to a character.
/// The character's live counter is <see cref="Character.CreditsAtCurrentLevel"/>;
/// these rows are the permanent history.
/// </summary>
public class SessionCredit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CharacterId { get; set; }
    public Character Character { get; set; } = default!;

    public Guid SessionId { get; set; }

    /// <summary>Character level at the moment the credit was awarded.</summary>
    public int LevelAtAward { get; set; }

    /// <summary>True when this credit completed the requirement and triggered an advancement.</summary>
    public bool TriggeredLevelUp { get; set; }

    public DateTimeOffset AwardedAt { get; set; } = DateTimeOffset.UtcNow;
}
