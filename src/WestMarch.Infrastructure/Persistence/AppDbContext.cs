using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WestMarch.Application.Common;
using WestMarch.Domain.Adventures;
using WestMarch.Domain.Announcements;
using WestMarch.Domain.Bestiary;
using WestMarch.Domain.Characters;
using WestMarch.Domain.Items;
using WestMarch.Domain.Sessions;
using WestMarch.Infrastructure.Identity;

namespace WestMarch.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IUnitOfWork
{
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<SessionCredit> SessionCredits => Set<SessionCredit>();
    public DbSet<Adventure> Adventures => Set<Adventure>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<RewardComponent> RewardComponents => Set<RewardComponent>();
    public DbSet<RewardOptionSet> RewardOptionSets => Set<RewardOptionSet>();
    public DbSet<RewardOption> RewardOptions => Set<RewardOption>();
    public DbSet<GameSession> Sessions => Set<GameSession>();
    public DbSet<SessionSignup> SessionSignups => Set<SessionSignup>();
    public DbSet<SessionMessage> SessionMessages => Set<SessionMessage>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ItemInstance> ItemInstances => Set<ItemInstance>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<MarketListing> MarketListings => Set<MarketListing>();
    public DbSet<Monster> Monsters => Set<Monster>();
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<EncounterNpc> EncounterNpcs => Set<EncounterNpc>();
    public DbSet<EncounterMonster> EncounterMonsters => Set<EncounterMonster>();

    Task IUnitOfWork.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    async Task<bool> IUnitOfWork.TrySaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Optimistic-concurrency loser (e.g. two buyers, one listing). Detach the
            // failed changes so the context stays usable for the caller's error path.
            ChangeTracker.Clear();
            return false;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
