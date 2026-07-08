using WestMarch.Domain.Bestiary;

namespace WestMarch.Application.Bestiary;

public record ParsedMonster(
    string Name,
    string ChallengeRating,
    decimal CrValue,
    int? Xp,
    int ArmorClass,
    int MaxHitPoints,
    string? HitDice,
    string Size,
    string CreatureType,
    string? Alignment,
    string StatsJson)
{
    public string ImportKey => Monster.MakeImportKey(Name);
}

public record ParsedMonsterFile(string? SourceNote, IReadOnlyList<ParsedMonster> Monsters);

/// <summary>
/// Parses the bestiary reference file (a flat JSON array of SRD monster records).
/// Implemented in Infrastructure.
/// </summary>
public interface IMonsterFileParser
{
    /// <summary>Throws AppValidationException when the payload doesn't match the expected shape.</summary>
    Task<ParsedMonsterFile> ParseAsync(Stream json, CancellationToken ct = default);
}
