namespace WestMarch.Domain.Adventures;

/// <summary>A "choose 1 of N" reward group attached to an adventure.</summary>
public class RewardOptionSet
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AdventureId { get; set; }

    /// <summary>Prompt shown to players, e.g. "Choose one boon of the Wardens".</summary>
    public string Name { get; set; } = default!;

    public int SortOrder { get; set; }

    public List<RewardOption> Options { get; set; } = [];
}
