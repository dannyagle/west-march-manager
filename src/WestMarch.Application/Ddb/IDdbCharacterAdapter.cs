namespace WestMarch.Application.Ddb;

/// <summary>
/// Best-effort stat header pulled from D&D Beyond's unsupported character service.
/// Every field is optional by design — render whatever came back.
/// </summary>
public record DdbStatHeader(
    string Name,
    string? ClassSummary,
    int? Level,
    int? ArmorClass,
    int? MaxHitPoints,
    int? PassivePerception,
    IReadOnlyDictionary<string, int>? SavingThrows,
    int? Gold,
    IReadOnlyList<string>? MagicItems,
    IReadOnlyList<string>? Spells);

/// <summary>
/// Optional, feature-flagged, isolated adapter over D&D Beyond's unsupported character JSON
/// endpoint. Never throws to callers: any failure (flag off, network error, schema drift,
/// private character) returns null and the UI degrades to the plain D&D Beyond link.
/// The core application must never depend on this working.
/// </summary>
public interface IDdbCharacterAdapter
{
    Task<DdbStatHeader?> TryGetStatHeaderAsync(long characterId, CancellationToken ct = default);
}
