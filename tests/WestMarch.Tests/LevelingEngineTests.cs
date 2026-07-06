using WestMarch.Domain.Characters;

namespace WestMarch.Tests;

public class LevelingEngineTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(9, 4)]
    [InlineData(12, 4)]
    [InlineData(13, 5)]
    [InlineData(16, 5)]
    [InlineData(17, 6)]
    [InlineData(20, 6)]
    public void ProficiencyBonus_matches_5e_table(int level, int expected) =>
        Assert.Equal(expected, LevelingEngine.ProficiencyBonus(level));

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-3)]
    public void ProficiencyBonus_rejects_out_of_range_levels(int level) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelingEngine.ProficiencyBonus(level));

    [Fact]
    public void Level3_character_needs_2_sessions_to_reach_4()
    {
        // Worked example from the campaign rules.
        Assert.Equal(2, LevelingEngine.SessionsRequiredToAdvance(3));

        var character = new Character { Name = "Fresh", Level = 3, OwnerUserId = "u", DdbUrl = "x" };

        var first = LevelingEngine.AwardSessionCredit(character);
        Assert.False(first.TriggeredLevelUp);
        Assert.Equal(3, character.Level);

        var second = LevelingEngine.AwardSessionCredit(character);
        Assert.True(second.TriggeredLevelUp);
        Assert.Equal(4, character.Level);
        Assert.Equal(0, character.CreditsAtCurrentLevel);
    }

    [Fact]
    public void Level11_character_needs_4_sessions_to_reach_12()
    {
        // Worked example from the campaign rules.
        Assert.Equal(4, LevelingEngine.SessionsRequiredToAdvance(11));

        var character = new Character { Name = "Veteran", Level = 11, OwnerUserId = "u", DdbUrl = "x" };

        for (var i = 0; i < 3; i++)
        {
            Assert.False(LevelingEngine.AwardSessionCredit(character).TriggeredLevelUp);
        }

        Assert.Equal(11, character.Level);
        Assert.Equal(3, character.CreditsAtCurrentLevel);

        Assert.True(LevelingEngine.AwardSessionCredit(character).TriggeredLevelUp);
        Assert.Equal(12, character.Level);
    }

    [Fact]
    public void Progress_display_reads_naturally()
    {
        var progress = LevelingEngine.GetProgress(11, 1);
        Assert.Equal("1 of 4 sessions toward level 12", progress.Display);
        Assert.Equal(12, progress.NextLevel);
        Assert.False(progress.IsMaxLevel);
    }

    [Fact]
    public void Requirement_is_read_from_the_new_level_after_advancing()
    {
        // Reaching level 5 moves the character into the +3 tier: 3 sessions to level 6.
        var character = new Character { Name = "Climber", Level = 4, CreditsAtCurrentLevel = 1, OwnerUserId = "u", DdbUrl = "x" };

        Assert.True(LevelingEngine.AwardSessionCredit(character).TriggeredLevelUp);
        Assert.Equal(5, character.Level);
        Assert.Equal(3, LevelingEngine.GetProgress(character).CreditsRequired);
    }

    [Fact]
    public void Max_level_characters_log_credits_but_never_advance()
    {
        var character = new Character { Name = "Legend", Level = 20, OwnerUserId = "u", DdbUrl = "x" };

        var result = LevelingEngine.AwardSessionCredit(character);

        Assert.False(result.TriggeredLevelUp);
        Assert.Equal(20, character.Level);
        Assert.True(LevelingEngine.GetProgress(character).IsMaxLevel);
    }
}
