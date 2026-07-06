using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using WestMarch.Application.Common;
using WestMarch.Domain.Users;

namespace WestMarch.Web.Services;

/// <summary>
/// ICurrentUser over the Blazor authentication state. Works in both interactive
/// circuits and static server rendering; falls back to HttpContext.User outside
/// a component context (e.g. minimal API endpoints).
/// </summary>
public class CurrentUserService(
    AuthenticationStateProvider authStateProvider,
    IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? _cached;

    private ClaimsPrincipal? Principal
    {
        get
        {
            if (_cached is not null)
            {
                return _cached;
            }

            // GetAuthenticationStateAsync completes synchronously once the circuit is up;
            // during the very first prerender it may not, so fall back to HttpContext.
            var task = authStateProvider.GetAuthenticationStateAsync();
            var principal = task.IsCompletedSuccessfully
                ? task.Result.User
                : httpContextAccessor.HttpContext?.User;

            if (principal?.Identity?.IsAuthenticated == true)
            {
                _cached = principal;
            }

            return principal;
        }
    }

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? DisplayName => Principal?.Identity?.Name;

    public bool IsDm => IsInRole(Roles.DungeonMaster) || IsCa;

    public bool IsCa => IsInRole(Roles.CampaignAdmin);

    private bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}
