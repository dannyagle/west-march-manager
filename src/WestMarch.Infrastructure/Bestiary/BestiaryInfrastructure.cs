using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WestMarch.Application.Bestiary;
using WestMarch.Domain.Adventures;
using WestMarch.Domain.Bestiary;
using WestMarch.Domain.Items;
using WestMarch.Infrastructure.Persistence;

namespace WestMarch.Infrastructure.Bestiary;

// ---------- EF configurations ----------

public class MonsterConfiguration : IEntityTypeConfiguration<Monster>
{
    public void Configure(EntityTypeBuilder<Monster> b)
    {
        b.Property(m => m.Name).HasMaxLength(150).IsRequired();
        b.Property(m => m.ChallengeRating).HasMaxLength(10).IsRequired();
        b.Property(m => m.CrValue).HasPrecision(6, 3);
        b.Property(m => m.HitDice).HasMaxLength(30);
        b.Property(m => m.Size).HasMaxLength(30);
        b.Property(m => m.CreatureType).HasMaxLength(50);
        b.Property(m => m.Alignment).HasMaxLength(50);
        b.Property(m => m.ImportKey).HasMaxLength(160).IsRequired();
        b.Property(m => m.StatsJson).IsRequired();

        b.HasIndex(m => m.ImportKey).IsUnique();
        b.HasIndex(m => new { m.IsActive, m.CrValue });
    }
}

public class EncounterConfiguration : IEntityTypeConfiguration<Encounter>
{
    public void Configure(EntityTypeBuilder<Encounter> b)
    {
        b.Property(e => e.Title).HasMaxLength(150).IsRequired();

        b.HasMany(e => e.Npcs)
            .WithOne()
            .HasForeignKey(n => n.EncounterId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(e => e.Monsters)
            .WithOne()
            .HasForeignKey(m => m.EncounterId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(e => e.AdventureId);
    }
}

public class EncounterNpcConfiguration : IEntityTypeConfiguration<EncounterNpc>
{
    public void Configure(EntityTypeBuilder<EncounterNpc> b)
    {
        b.Property(n => n.Name).HasMaxLength(100).IsRequired();
        b.Property(n => n.Stats).HasMaxLength(500);
        b.Property(n => n.Description).HasMaxLength(2000);
    }
}

public class EncounterMonsterConfiguration : IEntityTypeConfiguration<EncounterMonster>
{
    public void Configure(EntityTypeBuilder<EncounterMonster> b)
    {
        // Monsters are never hard-deleted (imports deactivate), so Restrict is safe.
        b.HasOne(m => m.Monster)
            .WithMany()
            .HasForeignKey(m => m.MonsterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// ---------- repository ----------

public class MonsterRepository(AppDbContext db) : IMonsterRepository
{
    public Task<Monster?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Monsters.FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlyList<Monster>> ListAsync(MonsterFilter filter, CancellationToken ct = default)
    {
        var query = db.Monsters.AsNoTracking();

        if (!filter.IncludeInactive)
        {
            query = query.Where(m => m.IsActive);
        }

        if (filter.MaxCr is not null)
        {
            query = query.Where(m => m.CrValue <= filter.MaxCr);
        }

        if (!string.IsNullOrWhiteSpace(filter.CreatureType))
        {
            query = query.Where(m => m.CreatureType == filter.CreatureType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            query = query.Where(m => m.Name.Contains(s));
        }

        return await query.OrderBy(m => m.CrValue).ThenBy(m => m.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Monster>> ListByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var wanted = ids.Distinct().ToList();
        return wanted.Count == 0
            ? []
            : await db.Monsters.AsNoTracking().Where(m => wanted.Contains(m.Id)).ToListAsync(ct);
    }

    public Task<Dictionary<string, Monster>> GetByKeyAsync(CancellationToken ct = default) =>
        db.Monsters.ToDictionaryAsync(m => m.ImportKey, ct);

    public void Add(Monster monster) => db.Monsters.Add(monster);

    public void AddBatch(ImportBatch batch) => db.ImportBatches.Add(batch);
}
