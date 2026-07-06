using System.Text.RegularExpressions;

namespace WestMarch.Application.Characters;

/// <summary>
/// Validates and parses D&D Beyond character page URLs,
/// e.g. https://www.dndbeyond.com/characters/12345678.
/// </summary>
public static partial class DdbUrlParser
{
    [GeneratedRegex(@"^https?://(www\.)?dndbeyond\.com/characters/(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CharacterUrl();

    public static bool IsValid(string? url) => url is not null && CharacterUrl().IsMatch(url.Trim());

    /// <summary>Extracts the numeric character id when the URL is a valid DDB character link.</summary>
    public static long? TryGetCharacterId(string? url)
    {
        if (url is null)
        {
            return null;
        }

        var match = CharacterUrl().Match(url.Trim());
        return match.Success && long.TryParse(match.Groups["id"].Value, out var id) ? id : null;
    }
}
