using Microsoft.AspNetCore.Identity;

namespace WestMarch.Infrastructure.Identity;

/// <summary>
/// The core principal. Identity's login-linkage tables (AspNetUserLogins) give us
/// one User ↔ many external identities: Discord today, more providers later, plus an
/// optional local password — all against this single user row. Domain entities reference
/// users only by id and never see this type.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Name shown across the app. Defaults from the provider (e.g. Discord global name) or registration.</summary>
    public string DisplayName { get; set; } = default!;

    /// <summary>Avatar URL from an external provider, when available.</summary>
    public string? AvatarUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
