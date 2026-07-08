namespace WestMarch.Domain.Bestiary;

/// <summary>
/// One bestiary entry, imported from a CA-refreshable reference file (same lifecycle as
/// the item catalog: upsert by name, deactivate-not-delete, diff preview). The columns
/// carry what lists and CR filtering need; <see cref="StatsJson"/> keeps the complete
/// original record so stat blocks render every trait, action, and legendary action
/// without a column per field.
/// </summary>
public class Monster
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = default!;

    /// <summary>Display form, e.g. "1/4" or "10".</summary>
    public string ChallengeRating { get; set; } = "0";

    /// <summary>Numeric CR for filtering and sorting (1/4 → 0.25).</summary>
    public decimal CrValue { get; set; }

    public int? Xp { get; set; }

    public int ArmorClass { get; set; }

    public int MaxHitPoints { get; set; }

    public string? HitDice { get; set; }

    /// <summary>e.g. "large".</summary>
    public string Size { get; set; } = "";

    /// <summary>e.g. "aberration", "beast".</summary>
    public string CreatureType { get; set; } = "";

    public string? Alignment { get; set; }

    /// <summary>The complete source record (abilities, saves, skills, traits, actions, …) as JSON.</summary>
    public string StatsJson { get; set; } = "{}";

    /// <summary>Stable identity across imports: normalized name.</summary>
    public string ImportKey { get; set; } = default!;

    public Guid? LastImportBatchId { get; set; }

    /// <summary>False when a re-import no longer contains the monster; never deleted (encounters reference it).</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static string MakeImportKey(string name) => name.Trim().ToLowerInvariant();
}
