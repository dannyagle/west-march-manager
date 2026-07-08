using WestMarch.Application.Common;
using WestMarch.Domain.Bestiary;
using WestMarch.Domain.Items;

namespace WestMarch.Application.Bestiary;

public record MonsterImportChange(string Name, string ChallengeRating, string Detail);

public record MonsterImportPreview(
    IReadOnlyList<MonsterImportChange> Added,
    IReadOnlyList<MonsterImportChange> Updated,
    IReadOnlyList<MonsterImportChange> Deactivated,
    int UnchangedCount);

public interface IMonsterService
{
    /// <summary>Bestiary browsing/picking — DM or CA.</summary>
    Task<IReadOnlyList<Monster>> ListAsync(MonsterFilter filter, CancellationToken ct = default);

    Task<Monster> GetAsync(Guid id, CancellationToken ct = default);

    Task<MonsterImportPreview> PreviewImportAsync(ParsedMonsterFile file, CancellationToken ct = default);
    Task<ImportBatch> ApplyImportAsync(ParsedMonsterFile file, string fileName, CancellationToken ct = default);
}

/// <summary>
/// The campaign bestiary: viewable by DMs (it feeds the encounter builder and the DM screen),
/// refreshed by CAs with the same import semantics as the item catalog — upsert by name,
/// deactivate entries missing from the new file, never delete (encounters reference them).
/// </summary>
public class MonsterService(
    IMonsterRepository monsters,
    IUnitOfWork uow,
    ICurrentUser currentUser) : IMonsterService
{
    public Task<IReadOnlyList<Monster>> ListAsync(MonsterFilter filter, CancellationToken ct = default)
    {
        RequireDm();
        return monsters.ListAsync(filter, ct);
    }

    public async Task<Monster> GetAsync(Guid id, CancellationToken ct = default)
    {
        RequireDm();
        return await monsters.GetAsync(id, ct) ?? throw new NotFoundException(nameof(Monster), id);
    }

    public async Task<MonsterImportPreview> PreviewImportAsync(ParsedMonsterFile file, CancellationToken ct = default)
    {
        RequireCa();

        var existing = await monsters.GetByKeyAsync(ct);
        var incomingKeys = file.Monsters.Select(m => m.ImportKey).ToHashSet();

        var added = new List<MonsterImportChange>();
        var updated = new List<MonsterImportChange>();
        var unchanged = 0;

        foreach (var parsed in file.Monsters)
        {
            if (!existing.TryGetValue(parsed.ImportKey, out var current))
            {
                added.Add(new MonsterImportChange(parsed.Name, parsed.ChallengeRating,
                    $"CR {parsed.ChallengeRating}, AC {parsed.ArmorClass}, {parsed.MaxHitPoints} HP"));
            }
            else if (HasChanges(current, parsed))
            {
                updated.Add(new MonsterImportChange(parsed.Name, parsed.ChallengeRating, DescribeDiff(current, parsed)));
            }
            else
            {
                unchanged++;
            }
        }

        var deactivated = existing.Values
            .Where(m => m.IsActive && !incomingKeys.Contains(m.ImportKey))
            .Select(m => new MonsterImportChange(m.Name, m.ChallengeRating, "no longer in the source file"))
            .OrderBy(c => c.Name)
            .ToList();

        return new MonsterImportPreview(added, updated, deactivated, unchanged);
    }

    public async Task<ImportBatch> ApplyImportAsync(ParsedMonsterFile file, string fileName, CancellationToken ct = default)
    {
        RequireCa();

        if (file.Monsters.Count == 0)
        {
            throw new AppValidationException("The file contains no monsters — refusing to deactivate the whole bestiary.");
        }

        var existing = await monsters.GetByKeyAsync(ct);
        var incomingKeys = file.Monsters.Select(m => m.ImportKey).ToHashSet();

        var batch = new ImportBatch
        {
            Kind = ImportFileKind.Monster,
            FileName = fileName,
            SourceNote = file.SourceNote,
            UploadedByUserId = currentUser.RequireUserId(),
        };

        foreach (var parsed in file.Monsters)
        {
            if (existing.TryGetValue(parsed.ImportKey, out var current))
            {
                if (HasChanges(current, parsed))
                {
                    Apply(current, parsed);
                    current.LastImportBatchId = batch.Id;
                    current.UpdatedAt = DateTimeOffset.UtcNow;
                    batch.UpdatedCount++;
                }
                else
                {
                    batch.UnchangedCount++;
                }
            }
            else
            {
                var monster = new Monster
                {
                    ImportKey = parsed.ImportKey,
                    LastImportBatchId = batch.Id,
                };
                Apply(monster, parsed);
                monsters.Add(monster);
                batch.AddedCount++;
            }
        }

        foreach (var gone in existing.Values.Where(m => m.IsActive && !incomingKeys.Contains(m.ImportKey)))
        {
            gone.IsActive = false;
            gone.UpdatedAt = DateTimeOffset.UtcNow;
            batch.DeactivatedCount++;
        }

        monsters.AddBatch(batch);
        await uow.SaveChangesAsync(ct);
        return batch;
    }

    private static void Apply(Monster monster, ParsedMonster parsed)
    {
        monster.Name = parsed.Name;
        monster.ChallengeRating = parsed.ChallengeRating;
        monster.CrValue = parsed.CrValue;
        monster.Xp = parsed.Xp;
        monster.ArmorClass = parsed.ArmorClass;
        monster.MaxHitPoints = parsed.MaxHitPoints;
        monster.HitDice = parsed.HitDice;
        monster.Size = parsed.Size;
        monster.CreatureType = parsed.CreatureType;
        monster.Alignment = parsed.Alignment;
        monster.StatsJson = parsed.StatsJson;
    }

    private static bool HasChanges(Monster monster, ParsedMonster parsed) =>
        monster.Name != parsed.Name
        || monster.ChallengeRating != parsed.ChallengeRating
        || monster.ArmorClass != parsed.ArmorClass
        || monster.MaxHitPoints != parsed.MaxHitPoints
        || monster.StatsJson != parsed.StatsJson;

    private static string DescribeDiff(Monster current, ParsedMonster parsed)
    {
        var notes = new List<string>();
        if (current.ChallengeRating != parsed.ChallengeRating)
        {
            notes.Add($"CR {current.ChallengeRating} → {parsed.ChallengeRating}");
        }
        if (current.ArmorClass != parsed.ArmorClass)
        {
            notes.Add($"AC {current.ArmorClass} → {parsed.ArmorClass}");
        }
        if (current.MaxHitPoints != parsed.MaxHitPoints)
        {
            notes.Add($"HP {current.MaxHitPoints} → {parsed.MaxHitPoints}");
        }
        return notes.Count > 0 ? string.Join(", ", notes) : "stat block details changed";
    }

    private void RequireDm()
    {
        if (!currentUser.IsDm)
        {
            throw new ForbiddenAccessException("DM role required to view the bestiary.");
        }
    }

    private void RequireCa()
    {
        if (!currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Campaign Admin role required to refresh the bestiary.");
        }
    }
}
