namespace WestMarch.Domain.Items;

/// <summary>
/// Audit record of one CA file import: who uploaded which file, when, and what changed.
/// "Removing the old file and adding a new one" is applying a new batch —
/// data is upserted/deactivated, never wiped.
/// </summary>
public class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ItemKind Kind { get; set; }

    public string FileName { get; set; } = default!;

    /// <summary>Source/license note carried from the file header (e.g. CC BY-SA attribution).</summary>
    public string? SourceNote { get; set; }

    public string UploadedByUserId { get; set; } = default!;

    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    public int AddedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int DeactivatedCount { get; set; }
    public int UnchangedCount { get; set; }
}
