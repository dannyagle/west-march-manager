namespace WestMarch.Domain.Adventures;

public enum RewardKind
{
    Gold = 0,
    Item = 1,
    Other = 2,
}

/// <summary>A guaranteed reward every completing character receives, e.g. a gold amount.</summary>
public class RewardComponent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AdventureId { get; set; }

    public RewardKind Kind { get; set; }

    /// <summary>Gold pieces when <see cref="Kind"/> is Gold.</summary>
    public int? GoldAmount { get; set; }

    public string Description { get; set; } = default!;

    public int SortOrder { get; set; }
}
