namespace WestMarch.Domain.Items;

/// <summary>
/// Maps item rarity to the character-level tier it is appropriate for.
/// Aligned with the proficiency-bonus tiers used by the LevelingEngine:
/// Common 1–4, Uncommon 5–8, Rare 9–12, Very Rare 13–16, Legendary/Artifact 17–20.
/// Used to filter the adventure reward picker and to flag (never hard-block)
/// out-of-tier rewards and marketplace purchases.
/// </summary>
public static class RarityTiers
{
    /// <summary>The character-level range this rarity is appropriate for.</summary>
    public static (int MinLevel, int MaxLevel) LevelRange(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => (1, 4),
        ItemRarity.Uncommon => (5, 8),
        ItemRarity.Rare => (9, 12),
        ItemRarity.VeryRare => (13, 16),
        ItemRarity.Legendary => (17, 20),
        ItemRarity.Artifact => (17, 20),
        _ => (1, 20), // mundane: always appropriate
    };

    /// <summary>True when the rarity's tier overlaps the given level range (e.g. an adventure's).</summary>
    public static bool IsAppropriateFor(ItemRarity rarity, int minLevel, int maxLevel)
    {
        var (tierMin, tierMax) = LevelRange(rarity);
        return tierMin <= maxLevel && tierMax >= minLevel;
    }

    /// <summary>True when the rarity's tier contains a single character's level.</summary>
    public static bool IsAppropriateFor(ItemRarity rarity, int characterLevel) =>
        IsAppropriateFor(rarity, characterLevel, characterLevel);

    /// <summary>Rarities whose tier overlaps the given level range, in ascending order.</summary>
    public static IReadOnlyList<ItemRarity> AppropriateRarities(int minLevel, int maxLevel) =>
        [.. new[]
            {
                ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare,
                ItemRarity.VeryRare, ItemRarity.Legendary, ItemRarity.Artifact,
            }
            .Where(r => IsAppropriateFor(r, minLevel, maxLevel))];
}
