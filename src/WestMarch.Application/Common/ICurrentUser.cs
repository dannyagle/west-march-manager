namespace WestMarch.Application.Common;

/// <summary>
/// Ambient identity of the caller, abstracted away from ASP.NET Core so
/// application services can enforce authorization and stay unit-testable.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    string? UserId { get; }

    string? DisplayName { get; }

    /// <summary>True when the user holds the DM role — or CA, which can do everything a DM can.</summary>
    bool IsDm { get; }

    /// <summary>True when the user holds the Campaign Admin role.</summary>
    bool IsCa { get; }
}

public static class CurrentUserExtensions
{
    /// <summary>Returns the user id or throws when unauthenticated.</summary>
    public static string RequireUserId(this ICurrentUser user) =>
        user.IsAuthenticated && user.UserId is not null
            ? user.UserId
            : throw new ForbiddenAccessException("Authentication required.");
}
