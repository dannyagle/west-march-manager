using System.Text.Json;
using WestMarch.Application.Bestiary;
using WestMarch.Application.Common;
using WestMarch.Domain.Bestiary;

namespace WestMarch.Infrastructure.Bestiary;

/// <summary>
/// Parses the bestiary reference file: a flat JSON array of SRD monster records
/// (name, ac, maxHitPoints, size, creatureType, challenge{rating,xp}, plus full
/// stat detail). The complete record is preserved verbatim as StatsJson so the
/// DM screen renders every trait and action without a schema per field.
/// </summary>
public class MonsterJsonParser : IMonsterFileParser
{
    public async Task<ParsedMonsterFile> ParseAsync(Stream json, CancellationToken ct = default)
    {
        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(json, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw new AppValidationException($"That file is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new AppValidationException(
                    "Unexpected file shape: expected a JSON array of monster records.");
            }

            var parsed = new List<ParsedMonster>();
            foreach (var item in root.EnumerateArray())
            {
                var name = GetString(item, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new AppValidationException("A monster in the file has no name — refusing to import.");
                }

                string rating = "0";
                int? xp = null;
                if (item.TryGetProperty("challenge", out var challenge) && challenge.ValueKind == JsonValueKind.Object)
                {
                    rating = GetString(challenge, "rating") ?? "0";
                    xp = GetInt(challenge, "xp");
                }

                parsed.Add(new ParsedMonster(
                    name.Trim(),
                    rating,
                    ChallengeRatings.Parse(rating),
                    xp,
                    GetInt(item, "ac") ?? 10,
                    GetInt(item, "maxHitPoints") ?? 1,
                    GetString(item, "hitDice"),
                    GetString(item, "size") ?? "",
                    GetString(item, "creatureType") ?? "",
                    GetString(item, "alignment"),
                    item.GetRawText()));
            }

            var duplicate = parsed.GroupBy(p => p.ImportKey).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
            {
                throw new AppValidationException(
                    $"The file lists \"{duplicate.First().Name}\" more than once — clean it up and retry.");
            }

            return new ParsedMonsterFile(SourceNote: null, parsed);
        }
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
}
