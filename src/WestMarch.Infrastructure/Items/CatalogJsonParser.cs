using System.Text.Json;
using WestMarch.Application.Common;
using WestMarch.Application.Items;
using WestMarch.Domain.Items;

namespace WestMarch.Infrastructure.Items;

/// <summary>
/// Parses the campaign's two reference-file shapes:
/// magic-items.json (items[]: name, rarity, type, requires_attunement, price{raw,gp,base_item_plus,special}, url, multi_rarity)
/// and equipment.json (items[]: name, category, cost{raw,gp,special}, source_url, + per-category detail columns).
/// Defensive throughout — a malformed file yields a user-facing validation error, never a crash.
/// </summary>
public class CatalogJsonParser : ICatalogFileParser
{
    public async Task<ParsedCatalogFile> ParseAsync(Stream json, ItemKind kind, CancellationToken ct = default)
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

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                throw new AppValidationException(
                    "Unexpected file shape: expected a top-level object with an \"items\" array.");
            }

            var sourceNote = string.Join(" — ", new[]
            {
                GetString(root, "source"),
                GetString(root, "source_license"),
                GetString(root, "generated_utc") is { } g ? $"generated {g}" : null,
            }.Where(s => !string.IsNullOrEmpty(s)));

            var parsed = new List<ParsedCatalogItem>();
            foreach (var item in items.EnumerateArray())
            {
                var name = GetString(item, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new AppValidationException("An item in the file has no name — refusing to import.");
                }

                parsed.Add(kind == ItemKind.Magic ? ParseMagicItem(item, name) : ParseEquipment(item, name));
            }

            // The import key must be unique within the file or the upsert would be ambiguous.
            var duplicate = parsed.GroupBy(p => p.ImportKey).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
            {
                throw new AppValidationException(
                    $"The file lists \"{duplicate.First().Name}\" more than once at the same rarity — clean it up and retry.");
            }

            return new ParsedCatalogFile(kind, string.IsNullOrEmpty(sourceNote) ? null : sourceNote, parsed);
        }
    }

    private static ParsedCatalogItem ParseMagicItem(JsonElement item, string name)
    {
        var priceRaw = (string?)null;
        int? gp = null;
        var basePlus = false;

        if (item.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Object)
        {
            priceRaw = GetString(price, "raw");
            gp = GetWholeGp(price);
            basePlus = price.TryGetProperty("base_item_plus", out var bp) && bp.ValueKind == JsonValueKind.True;
        }

        string? detailsJson = null;
        if (item.TryGetProperty("multi_rarity", out var mr) && mr.ValueKind == JsonValueKind.True)
        {
            detailsJson = """{"multi_rarity":true}""";
        }

        return new ParsedCatalogItem(
            name.Trim(),
            ItemRarityExtensions.ParseRarity(GetString(item, "rarity")),
            GetString(item, "type") ?? "",
            item.TryGetProperty("requires_attunement", out var att) && att.ValueKind == JsonValueKind.True,
            gp,
            priceRaw,
            basePlus,
            GetString(item, "url"),
            detailsJson);
    }

    private static ParsedCatalogItem ParseEquipment(JsonElement item, string name)
    {
        var priceRaw = (string?)null;
        int? gp = null;

        if (item.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Object)
        {
            priceRaw = GetString(cost, "raw");
            gp = GetWholeGp(cost);
        }

        // Keep every extra source column (damage, AC, weight, …) for display.
        var details = new Dictionary<string, object?>();
        foreach (var property in item.EnumerateObject())
        {
            if (property.Name is "name" or "category" or "cost" or "source_url")
            {
                continue;
            }

            details[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => property.Value.EnumerateArray()
                    .Where(v => v.ValueKind == JsonValueKind.String)
                    .Select(v => v.GetString())
                    .ToArray(),
                _ => null,
            };
        }

        var nonNull = details.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);

        return new ParsedCatalogItem(
            name.Trim(),
            ItemRarity.None,
            GetString(item, "category") ?? "",
            RequiresAttunement: false,
            gp,
            priceRaw,
            PriceIsBasePlus: false,
            GetString(item, "source_url"),
            nonNull.Count > 0 ? JsonSerializer.Serialize(nonNull) : null);
    }

    /// <summary>Whole gold pieces only; fractional prices (5 SP) keep their raw string but no numeric value.</summary>
    private static int? GetWholeGp(JsonElement priceObject)
    {
        if (!priceObject.TryGetProperty("gp", out var gp) || gp.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var value = gp.GetDouble();
        return value >= 1 && value == Math.Floor(value) ? (int)value : null;
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
