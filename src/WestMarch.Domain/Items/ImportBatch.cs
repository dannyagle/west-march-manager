namespace WestMarch.Domain.Items;

/// <summary>Which reference file an import batch refreshed. Member names double as the
/// stored strings (so Magic/Mundane rows written before monsters existed still parse),
/// and Mundane/Magic keep ItemKind's numeric values for safe casting.</summary>
public enum ImportFileKind
{
    Mundane = 0,
    Magic = 1,
    Monster = 2,
}

/// <summary>
/// Audit record of one CA file import: who uploaded which file, when, and what changed.
/// "Removing the old file and adding a new one" is applying a new batch —
/// data is upserted/deactivated, never wiped. Covers item files and the bestiary alike.
/// </summary>
public class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ImportFileKind Kind { get; set; }

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
