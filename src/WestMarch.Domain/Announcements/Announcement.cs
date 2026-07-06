namespace WestMarch.Domain.Announcements;

/// <summary>CA-authored announcement/blog entry surfaced on the main page during its active window.</summary>
public class Announcement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = default!;

    /// <summary>Markdown body; may embed uploaded images.</summary>
    public string Body { get; set; } = default!;

    public string AuthorUserId { get; set; } = default!;

    public DateTimeOffset ActiveFrom { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Null means displayed indefinitely.</summary>
    public DateTimeOffset? ActiveUntil { get; set; }

    /// <summary>Optional ordering preference; lower sorts first, ties fall back to newest-first.</summary>
    public int? SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActiveOn(DateTimeOffset date) =>
        date >= ActiveFrom && (ActiveUntil is null || date <= ActiveUntil);
}
