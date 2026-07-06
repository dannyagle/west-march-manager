namespace WestMarch.Domain.Adventures;

public class Adventure
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = default!;

    public string AuthorUserId { get; set; } = default!;

    public int MinLevel { get; set; } = 3;
    public int MaxLevel { get; set; } = 5;

    /// <summary>Target party size; campaign default is 4–6 characters.</summary>
    public int TargetPlayersMin { get; set; } = 4;
    public int TargetPlayersMax { get; set; } = 6;

    /// <summary>Public teaser shown on cards and the quest board.</summary>
    public string ShortDescription { get; set; } = default!;

    /// <summary>Public full description.</summary>
    public string LongDescription { get; set; } = default!;

    /// <summary>Visible to DMs and CAs only.</summary>
    public string? DmNotes { get; set; }

    /// <summary>Monster stat blocks needed to run the session. DM/CA only.</summary>
    public string? MonsterStatBlocks { get; set; }

    /// <summary>Start of the window in which this adventure may be scheduled.</summary>
    public DateTimeOffset ActiveFrom { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Null means active forever.</summary>
    public DateTimeOffset? ActiveUntil { get; set; }

    public AdventureStatus Status { get; set; } = AdventureStatus.Draft;

    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Tag> Tags { get; set; } = [];
    public List<RewardComponent> GuaranteedRewards { get; set; } = [];
    public List<RewardOptionSet> RewardOptionSets { get; set; } = [];

    public bool IsActiveOn(DateTimeOffset date) =>
        date >= ActiveFrom && (ActiveUntil is null || date <= ActiveUntil);

    public bool IsLevelInRange(int level) => level >= MinLevel && level <= MaxLevel;
}
