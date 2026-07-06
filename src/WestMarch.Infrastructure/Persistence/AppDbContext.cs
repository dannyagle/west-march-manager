using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WestMarch.Application.Common;
using WestMarch.Domain.Adventures;
using WestMarch.Domain.Announcements;
using WestMarch.Domain.Characters;
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

    Task IUnitOfWork.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
