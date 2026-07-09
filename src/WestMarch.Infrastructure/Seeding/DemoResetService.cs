using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WestMarch.Application.Common;
using WestMarch.Infrastructure.Persistence;

namespace WestMarch.Infrastructure.Seeding;

/// <summary>
/// True only when the app runs in Development or with SeedDemoData=true (the hosted
/// test/demo site). Gates demo-only tooling like the full data reset.
/// </summary>
public record DemoMode(bool Enabled);

public interface IDemoResetService
{
    /// <summary>
    /// Wipes ALL campaign data — users, characters, adventures, sessions, economy, and
    /// reference data — and reseeds the full demo campaign with fresh, relative dates.
    /// CA only, demo mode only. The caller's own account is destroyed with the rest;
    /// they must sign back in with a seed account.
    /// </summary>
    Task ResetAsync(CancellationToken ct = default);
}

public class DemoResetService(
    AppDbContext db,
    ICurrentUser currentUser,
    DemoMode demoMode,
    IServiceProvider services,
    ILogger<DemoResetService> logger) : IDemoResetService
{
    public async Task ResetAsync(CancellationToken ct = default)
    {
        // Both guards are server-side on purpose: hiding the button is not a security boundary.
        if (!demoMode.Enabled)
        {
            throw new ForbiddenAccessException("The demo reset is only available in development or demo mode.");
        }

        if (!currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Campaign Admin role required to reset the demo data.");
        }

        logger.LogWarning("DEMO RESET initiated by user {UserId} — wiping all campaign data.", currentUser.UserId);

        // Delete in FK-safe order. Set-based deletes; cascades cover the child rows
        // (encounters/rewards via Adventures, Identity link tables via Users).
        await db.MarketListings.ExecuteDeleteAsync(ct);
        await db.LedgerEntries.ExecuteDeleteAsync(ct);
        await db.ItemInstances.ExecuteDeleteAsync(ct);
        await db.SessionMessages.ExecuteDeleteAsync(ct);
        await db.SessionCredits.ExecuteDeleteAsync(ct);
        await db.SessionSignups.ExecuteDeleteAsync(ct);
        await db.Sessions.ExecuteDeleteAsync(ct);
        await db.Adventures.ExecuteDeleteAsync(ct);   // cascades encounters, NPCs, rewards, tag links
        await db.Tags.ExecuteDeleteAsync(ct);
        await db.Characters.ExecuteDeleteAsync(ct);
        await db.Announcements.ExecuteDeleteAsync(ct);
        await db.Users.ExecuteDeleteAsync(ct);        // cascades roles/claims/logins link rows
        await db.CatalogItems.ExecuteDeleteAsync(ct);
        await db.Monsters.ExecuteDeleteAsync(ct);
        await db.ImportBatches.ExecuteDeleteAsync(ct);

        logger.LogWarning("DEMO RESET: wipe complete; reseeding…");

        // The seeder's empty-checks now all pass, so this rebuilds reference data and the
        // demo campaign with dates relative to now (fresh 'recently run' sessions, etc.).
        await DevDataSeeder.SeedAsync(services);

        logger.LogWarning("DEMO RESET complete.");
    }
}
