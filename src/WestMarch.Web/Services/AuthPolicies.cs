using Microsoft.AspNetCore.Authorization;
using WestMarch.Domain.Users;

namespace WestMarch.Web.Services;

/// <summary>Policy names and definitions, shared by the app pipeline and the test suite.</summary>
public static class AuthPolicies
{
    /// <summary>Satisfied by the DM role or the Campaign Admin role.</summary>
    public const string RequireDm = "RequireDM";

    /// <summary>Satisfied by the Campaign Admin role only.</summary>
    public const string RequireCa = "RequireCA";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(RequireDm, p => p.RequireRole(Roles.DungeonMaster, Roles.CampaignAdmin));
        options.AddPolicy(RequireCa, p => p.RequireRole(Roles.CampaignAdmin));
    }
}
