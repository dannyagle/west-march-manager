using WestMarch.Domain.Items;

namespace WestMarch.Tests;

public class RarityTiersTests
{
    [Theory]
    [InlineData(ItemRarity.Common, 1, 4)]
    [InlineData(ItemRarity.Uncommon, 5, 8)]
    [InlineData(ItemRarity.Rare, 9, 12)]
    [InlineData(ItemRarity.VeryRare, 13, 16)]
    [InlineData(ItemRarity.Legendary, 17, 20)]
    [InlineData(ItemRarity.Artifact, 17, 20)]
    public void Tiers_align_with_proficiency_ranges(ItemRarity rarity, int min, int max) =>
        Assert.Equal((min, max), RarityTiers.LevelRange(rarity));

    [Fact]
    public void A_levels_3_to_5_adventure_spans_common_and_uncommon()
    {
        // The adventure range overlaps two tiers: both rarities are appropriate.
        Assert.True(RarityTiers.IsAppropriateFor(ItemRarity.Common, 3, 5));
        Assert.True(RarityTiers.IsAppropriateFor(ItemRarity.Uncommon, 3, 5));
        Assert.False(RarityTiers.IsAppropriateFor(ItemRarity.Rare, 3, 5));

        Assert.Equal(
            [ItemRarity.Common, ItemRarity.Uncommon],
            RarityTiers.AppropriateRarities(3, 5));
    }

    [Fact]
    public void Mundane_items_fit_every_level()
    {
        Assert.True(RarityTiers.IsAppropriateFor(ItemRarity.None, 1, 1));
        Assert.True(RarityTiers.IsAppropriateFor(ItemRarity.None, 20, 20));
    }

    [Fact]
    public void Single_character_level_check_matches_the_tier()
    {
        Assert.True(RarityTiers.IsAppropriateFor(ItemRarity.Rare, 11));
        Assert.False(RarityTiers.IsAppropriateFor(ItemRarity.Rare, 5));
        Assert.False(RarityTiers.IsAppropriateFor(ItemRarity.Legendary, 12));
    }

    [Fact]
    public void Rarity_display_uses_spaced_names() =>
        Assert.Equal("Very Rare", ItemRarity.VeryRare.Display());

    [Theory]
    [InlineData("Very Rare", ItemRarity.VeryRare)]
    [InlineData("very-rare", ItemRarity.VeryRare)]
    [InlineData("Legendary", ItemRarity.Legendary)]
    [InlineData("Artifact", ItemRarity.Artifact)]
    [InlineData("", ItemRarity.None)]
    [InlineData("Weird Nonsense", ItemRarity.None)]
    public void Source_rarity_strings_parse(string input, ItemRarity expected) =>
        Assert.Equal(expected, ItemRarityExtensions.ParseRarity(input));
}
