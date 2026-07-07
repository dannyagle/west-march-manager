namespace WestMarch.Domain.Items;

public enum ItemKind
{
    Mundane = 0,
    Magic = 1,
}

public enum ItemRarity
{
    /// <summary>Mundane equipment has no rarity.</summary>
    None = 0,
    Common = 1,
    Uncommon = 2,
    Rare = 3,
    VeryRare = 4,
    Legendary = 5,
    Artifact = 6,
}

public static class ItemRarityExtensions
{
    /// <summary>Display form, e.g. "Very Rare".</summary>
    public static string Display(this ItemRarity rarity) => rarity switch
    {
        ItemRarity.None => "—",
        ItemRarity.VeryRare => "Very Rare",
        _ => rarity.ToString(),
    };

    /// <summary>Parses source-file rarity strings ("Very Rare", "very-rare", …). Null/empty → None.</summary>
    public static ItemRarity ParseRarity(string? value)
    {
        var normalized = (value ?? "").Replace(" ", "").Replace("-", "").Trim();
        return Enum.TryParse<ItemRarity>(normalized, ignoreCase: true, out var rarity)
            ? rarity
            : ItemRarity.None;
    }
}
