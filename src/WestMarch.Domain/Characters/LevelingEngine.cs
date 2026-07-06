namespace WestMarch.Domain.Characters;

/// <summary>
/// Encodes the campaign advancement rule: a character advances after completing a number of
/// successful sessions equal to its proficiency bonus at its current level.
/// Pure domain logic — no persistence, no framework dependencies.
/// </summary>
public static class LevelingEngine
{
    public const int MinLevel = 1;
    public const int MaxLevel = 20;
    public const int StartingLevel = 3;

    /// <summary>Standard 5e proficiency bonus: +2 (1–4), +3 (5–8), +4 (9–12), +5 (13–16), +6 (17–20).</summary>
    public static int ProficiencyBonus(int level) => level switch
    {
        >= MinLevel and <= 4 => 2,
        >= 5 and <= 8 => 3,
        >= 9 and <= 12 => 4,
        >= 13 and <= 16 => 5,
        >= 17 and <= MaxLevel => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, $"Level must be {MinLevel}–{MaxLevel}."),
    };

    /// <summary>Sessions required to advance from <paramref name="level"/> to the next level.</summary>
    public static int SessionsRequiredToAdvance(int level) => ProficiencyBonus(level);

    /// <summary>Current progress toward the character's next level.</summary>
    public static LevelProgress GetProgress(Character character) =>
        GetProgress(character.Level, character.CreditsAtCurrentLevel);

    public static LevelProgress GetProgress(int level, int creditsAtCurrentLevel)
    {
        if (level >= MaxLevel)
        {
            return new LevelProgress(level, creditsAtCurrentLevel, 0, IsMaxLevel: true);
        }

        return new LevelProgress(level, creditsAtCurrentLevel, SessionsRequiredToAdvance(level), IsMaxLevel: false);
    }

    /// <summary>
    /// Awards one successful-session credit to the character, advancing it when the
    /// requirement for its current level is met. Returns the outcome, including whether
    /// the credit triggered a level-up.
    /// </summary>
    public static CreditResult AwardSessionCredit(Character character)
    {
        var levelAtAward = character.Level;

        if (character.Level >= MaxLevel)
        {
            // Max-level characters still log the session but cannot advance.
            character.CreditsAtCurrentLevel++;
            return new CreditResult(levelAtAward, character.Level, TriggeredLevelUp: false);
        }

        character.CreditsAtCurrentLevel++;

        if (character.CreditsAtCurrentLevel >= SessionsRequiredToAdvance(character.Level))
        {
            character.Level++;
            character.CreditsAtCurrentLevel = 0;
            return new CreditResult(levelAtAward, character.Level, TriggeredLevelUp: true);
        }

        return new CreditResult(levelAtAward, character.Level, TriggeredLevelUp: false);
    }
}

/// <summary>Progress toward the next level, e.g. "1 of 4 sessions toward level 12".</summary>
public record LevelProgress(int CurrentLevel, int CreditsEarned, int CreditsRequired, bool IsMaxLevel)
{
    public int NextLevel => IsMaxLevel ? CurrentLevel : CurrentLevel + 1;

    public string Display => IsMaxLevel
        ? $"Level {CurrentLevel} — the summit"
        : $"{CreditsEarned} of {CreditsRequired} sessions toward level {NextLevel}";
}

public record CreditResult(int LevelAtAward, int NewLevel, bool TriggeredLevelUp);
