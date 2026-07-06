using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WestMarch.Application.Common;
using WestMarch.Application.Ddb;

namespace WestMarch.Infrastructure.Ddb;

public class DdbAdapterOptions
{
    public const string SectionName = "Ddb";

    public string BaseUrl { get; set; } = "https://character-service.dndbeyond.com/character/v5/character/";
    public int TimeoutSeconds { get; set; } = 6;
    public int CacheMinutes { get; set; } = 10;
    public int FailureCacheMinutes { get; set; } = 2;
}

/// <summary>
/// Best-effort adapter over D&D Beyond's unsupported character-service JSON. Every parse
/// is defensive: the endpoint has no contract and can change or vanish at any time.
/// All failures are logged at Debug and surface as null — never as an exception.
/// </summary>
public class DdbCharacterAdapter(
    HttpClient http,
    IMemoryCache cache,
    IOptions<FeatureFlags> features,
    IOptions<DdbAdapterOptions> options,
    ILogger<DdbCharacterAdapter> logger) : IDdbCharacterAdapter
{
    private static readonly string[] AbilityNames = ["STR", "DEX", "CON", "INT", "WIS", "CHA"];

    public async Task<DdbStatHeader?> TryGetStatHeaderAsync(long characterId, CancellationToken ct = default)
    {
        if (!features.Value.DdbStatHeaders)
        {
            return null;
        }

        var cacheKey = $"ddb-stat-header-{characterId}";
        if (cache.TryGetValue(cacheKey, out DdbStatHeader? cached))
        {
            return cached;
        }

        DdbStatHeader? header = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));

            using var response = await http.GetAsync($"{options.Value.BaseUrl}{characterId}", cts.Token);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
                header = Parse(doc.RootElement);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "DDB stat-header fetch failed for character {CharacterId}", characterId);
        }

        cache.Set(cacheKey, header, TimeSpan.FromMinutes(
            header is null ? options.Value.FailureCacheMinutes : options.Value.CacheMinutes));

        return header;
    }

    private static DdbStatHeader? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data))
        {
            return null;
        }

        var name = GetString(data, "name");
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // Classes: "Rogue 3 / Cleric 2", plus total level.
        string? classSummary = null;
        int? totalLevel = null;
        if (data.TryGetProperty("classes", out var classes) && classes.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            var levels = 0;
            foreach (var cls in classes.EnumerateArray())
            {
                var level = GetInt(cls, "level") ?? 0;
                levels += level;
                var className = cls.TryGetProperty("definition", out var def) ? GetString(def, "name") : null;
                if (className is not null)
                {
                    parts.Add($"{className} {level}");
                }
            }

            if (parts.Count > 0)
            {
                classSummary = string.Join(" / ", parts);
                totalLevel = levels;
            }
        }

        var abilityScores = ReadAbilityScores(data);
        var conMod = Modifier(abilityScores, 2);
        var wisMod = Modifier(abilityScores, 4);
        var profBonus = totalLevel is > 0 and <= 20 ? Domain.Characters.LevelingEngine.ProficiencyBonus(totalLevel.Value) : 2;

        // Hit points: base + con per level, honoring overrides when present.
        int? maxHp = GetInt(data, "overrideHitPoints");
        if (maxHp is null)
        {
            var baseHp = GetInt(data, "baseHitPoints");
            if (baseHp is not null)
            {
                maxHp = baseHp + (GetInt(data, "bonusHitPoints") ?? 0) + (conMod ?? 0) * (totalLevel ?? 0);
            }
        }

        var proficiencies = CollectProficiencySubTypes(data);

        int? passivePerception = wisMod is null
            ? null
            : 10 + wisMod + (proficiencies.Contains("perception") ? profBonus : 0);

        Dictionary<string, int>? saves = null;
        if (abilityScores is not null)
        {
            saves = [];
            for (var i = 0; i < 6; i++)
            {
                var mod = Modifier(abilityScores, i)!.Value;
                var subType = $"{FullAbilityName(i)}-saving-throws";
                saves[AbilityNames[i]] = mod + (proficiencies.Contains(subType) ? profBonus : 0);
            }
        }

        int? gold = null;
        if (data.TryGetProperty("currencies", out var currencies))
        {
            gold = GetInt(currencies, "gp");
        }

        var magicItems = new List<string>();
        if (data.TryGetProperty("inventory", out var inventory) && inventory.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in inventory.EnumerateArray())
            {
                if (item.TryGetProperty("definition", out var def)
                    && def.TryGetProperty("magic", out var magic) && magic.ValueKind == JsonValueKind.True)
                {
                    var itemName = GetString(def, "name");
                    if (itemName is not null)
                    {
                        magicItems.Add(itemName);
                    }
                }
            }
        }

        var spells = CollectSpells(data);

        return new DdbStatHeader(
            name,
            classSummary,
            totalLevel,
            GetInt(data, "armorClass"),
            maxHp,
            passivePerception,
            saves,
            gold,
            magicItems.Count > 0 ? magicItems : null,
            spells.Count > 0 ? spells : null);
    }

    /// <summary>Stats come as id 1–6 (STR–CHA); overrideValue wins over value.</summary>
    private static int[]? ReadAbilityScores(JsonElement data)
    {
        if (!data.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var scores = new int[6];
        Array.Fill(scores, 10);

        foreach (var stat in stats.EnumerateArray())
        {
            var id = GetInt(stat, "id");
            var value = GetInt(stat, "value");
            if (id is >= 1 and <= 6 && value is not null)
            {
                scores[id.Value - 1] = value.Value;
            }
        }

        if (data.TryGetProperty("overrideStats", out var overrides) && overrides.ValueKind == JsonValueKind.Array)
        {
            foreach (var stat in overrides.EnumerateArray())
            {
                var id = GetInt(stat, "id");
                var value = GetInt(stat, "value");
                if (id is >= 1 and <= 6 && value is not null)
                {
                    scores[id.Value - 1] = value.Value;
                }
            }
        }

        return scores;
    }

    private static HashSet<string> CollectProficiencySubTypes(JsonElement data)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!data.TryGetProperty("modifiers", out var modifiers) || modifiers.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var source in modifiers.EnumerateObject())
        {
            if (source.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var mod in source.Value.EnumerateArray())
            {
                if (string.Equals(GetString(mod, "type"), "proficiency", StringComparison.OrdinalIgnoreCase))
                {
                    var subType = GetString(mod, "subType");
                    if (subType is not null)
                    {
                        result.Add(subType);
                    }
                }
            }
        }

        return result;
    }

    private static List<string> CollectSpells(JsonElement data)
    {
        var spells = new List<string>();

        void AddFrom(JsonElement spellArray)
        {
            if (spellArray.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var spell in spellArray.EnumerateArray())
            {
                if (spell.TryGetProperty("definition", out var def))
                {
                    var spellName = GetString(def, "name");
                    if (spellName is not null && !spells.Contains(spellName))
                    {
                        spells.Add(spellName);
                    }
                }
            }
        }

        if (data.TryGetProperty("classSpells", out var classSpells) && classSpells.ValueKind == JsonValueKind.Array)
        {
            foreach (var cls in classSpells.EnumerateArray())
            {
                if (cls.TryGetProperty("spells", out var arr))
                {
                    AddFrom(arr);
                }
            }
        }

        if (data.TryGetProperty("spells", out var spellGroups) && spellGroups.ValueKind == JsonValueKind.Object)
        {
            foreach (var group in spellGroups.EnumerateObject())
            {
                AddFrom(group.Value);
            }
        }

        return spells;
    }

    private static int? Modifier(int[]? scores, int index) =>
        scores is null ? null : (int)Math.Floor((scores[index] - 10) / 2.0);

    private static string FullAbilityName(int index) => index switch
    {
        0 => "strength",
        1 => "dexterity",
        2 => "constitution",
        3 => "intelligence",
        4 => "wisdom",
        _ => "charisma",
    };

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
}
