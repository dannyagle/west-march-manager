using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WestMarch.Domain.Users;
using WestMarch.Infrastructure.Identity;
using WestMarch.Infrastructure.Persistence;

namespace WestMarch.Infrastructure.Seeding;

/// <summary>
/// Startup initialization for non-development environments: applies pending EF migrations,
/// ensures the additive roles exist, and bootstraps a single Campaign Admin from configuration
/// so a freshly provisioned database has a way in. It never seeds demo data — reference data
/// (items, bestiary) is loaded by a CA through the in-app import screens after first sign-in.
///
/// Bootstrap admin is read from the "InitialAdmin" section (App Service application settings):
///   InitialAdmin:Email, InitialAdmin:Password, InitialAdmin:DisplayName
/// The admin is created only when that email does not already exist, so restarts and
/// redeploys are safe and idempotent. Leave the password blank after first run and rotate it
/// through the app if you like — this code never downgrades or resets an existing account.
/// </summary>
public static class ProductionInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var config = sp.GetRequiredService<IConfiguration>();

        logger.LogInformation("Applying database migrations…");
        await db.Database.MigrateAsync();

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        await BootstrapAdminAsync(config, userManager, logger);
    }

    private static async Task BootstrapAdminAsync(
        IConfiguration config,
        UserManager<ApplicationUser> userManager,
        ILogger logger)
    {
        var email = config["InitialAdmin:Email"];
        var password = config["InitialAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation(
                "No InitialAdmin configured; skipping admin bootstrap. Set InitialAdmin:Email and " +
                "InitialAdmin:Password to create the first Campaign Admin.");
            return;
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            logger.LogInformation("InitialAdmin {Email} already exists; leaving it untouched.", email);
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true, // no SMTP wired yet; the operator vouches for this account
            DisplayName = config["InitialAdmin:DisplayName"] is { Length: > 0 } name ? name : "Campaign Admin",
        };

        var created = await userManager.CreateAsync(admin, password);
        if (!created.Succeeded)
        {
            // Most commonly a password that fails the Identity strength policy.
            logger.LogError("Failed to create InitialAdmin {Email}: {Errors}",
                email, string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, Roles.CampaignAdmin);
        logger.LogInformation("Created InitialAdmin {Email} with the Campaign Admin role.", email);
    }
}
