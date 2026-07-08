using System.Globalization;

namespace WestMarch.Domain.Bestiary;

/// <summary>Challenge-rating parsing and the default author-facing CR filter.</summary>
public static class ChallengeRatings
{
    /// <summary>Parses CR display strings ("1/4", "10") into a sortable value. Unparseable → 0.</summary>
    public static decimal Parse(string? rating)
    {
        var text = (rating ?? "").Trim();

        if (text.Contains('/'))
        {
            var parts = text.Split('/');
            if (parts.Length == 2
                && decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var numerator)
                && decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var denominator)
                && denominator != 0)
            {
                return numerator / denominator;
            }

            return 0;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    /// <summary>
    /// Default encounter-builder ceiling for an adventure's level range: the party's max level
    /// plus two, leaving headroom for boss monsters (a level 3–4 adventure sees up to CR 6)
    /// while keeping an Adult Black Dragon out of a starter dungeon. Authors can always
    /// override with the "show all CRs" toggle.
    /// </summary>
    public static decimal DefaultMaxCr(int adventureMaxLevel) => adventureMaxLevel + 2;
}
