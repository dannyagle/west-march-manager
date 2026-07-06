namespace WestMarch.Domain.Characters;

public class Character
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning player's user id.</summary>
    public string OwnerUserId { get; set; } = default!;

    public string Name { get; set; } = default!;

    /// <summary>Optional free-text blurb, e.g. "Half-orc Barbarian". Shown when the DDB adapter is off.</summary>
    public string? Summary { get; set; }

    /// <summary>Characters start at level 3 per campaign rules.</summary>
    public int Level { get; set; } = 3;

    /// <summary>Successful sessions completed at the current level; consumed on level-up.</summary>
    public int CreditsAtCurrentLevel { get; set; }

    /// <summary>Required link to the character's D&D Beyond page.</summary>
    public string DdbUrl { get; set; } = default!;

    /// <summary>Numeric DDB character id parsed from the URL; feeds the optional stat-header adapter.</summary>
    public long? DdbCharacterId { get; set; }

    public bool IsRetired { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<SessionCredit> Credits { get; set; } = [];
}
