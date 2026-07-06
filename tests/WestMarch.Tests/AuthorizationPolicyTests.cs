using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WestMarch.Domain.Users;
using WestMarch.Web.Services;

namespace WestMarch.Tests;

/// <summary>
/// Evaluates the real policy definitions (AuthPolicies.Configure) against principals
/// holding each combination of the additive role set.
/// </summary>
public class AuthorizationPolicyTests
{
    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(AuthPolicies.Configure);
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task Plain_player_is_denied_both_policies()
    {
        var authz = BuildAuthorizationService();
        var player = PrincipalWithRoles();

        Assert.False((await authz.AuthorizeAsync(player, AuthPolicies.RequireDm)).Succeeded);
        Assert.False((await authz.AuthorizeAsync(player, AuthPolicies.RequireCa)).Succeeded);
    }

    [Fact]
    public async Task Dm_passes_RequireDM_but_not_RequireCA()
    {
        var authz = BuildAuthorizationService();
        var dm = PrincipalWithRoles(Roles.DungeonMaster);

        Assert.True((await authz.AuthorizeAsync(dm, AuthPolicies.RequireDm)).Succeeded);
        Assert.False((await authz.AuthorizeAsync(dm, AuthPolicies.RequireCa)).Succeeded);
    }

    [Fact]
    public async Task Ca_passes_both_policies_without_holding_DM()
    {
        // "A CA can see and do everything" — including DM sections.
        var authz = BuildAuthorizationService();
        var ca = PrincipalWithRoles(Roles.CampaignAdmin);

        Assert.True((await authz.AuthorizeAsync(ca, AuthPolicies.RequireDm)).Succeeded);
        Assert.True((await authz.AuthorizeAsync(ca, AuthPolicies.RequireCa)).Succeeded);
    }

    [Fact]
    public async Task Roles_are_additive_a_user_can_hold_both()
    {
        var authz = BuildAuthorizationService();
        var both = PrincipalWithRoles(Roles.DungeonMaster, Roles.CampaignAdmin);

        Assert.True((await authz.AuthorizeAsync(both, AuthPolicies.RequireDm)).Succeeded);
        Assert.True((await authz.AuthorizeAsync(both, AuthPolicies.RequireCa)).Succeeded);
    }

    [Fact]
    public async Task Anonymous_principal_is_denied()
    {
        var authz = BuildAuthorizationService();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False((await authz.AuthorizeAsync(anonymous, AuthPolicies.RequireDm)).Succeeded);
        Assert.False((await authz.AuthorizeAsync(anonymous, AuthPolicies.RequireCa)).Succeeded);
    }
}
