namespace WestMarch.Application.Users;

/// <summary>
/// Read/administer users without exposing the identity framework to the application layer.
/// Implemented in Infrastructure over ASP.NET Core Identity.
/// </summary>
public interface IUserDirectory
{
    Task<IReadOnlyList<UserSummary>> SearchAsync(string? query, CancellationToken ct = default);

    Task<UserSummary?> GetAsync(string userId, CancellationToken ct = default);

    /// <summary>Batch display-name lookup for rendering authors, DMs, message posters.</summary>
    Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default);

    Task AddRoleAsync(string userId, string role, CancellationToken ct = default);

    Task RemoveRoleAsync(string userId, string role, CancellationToken ct = default);
}

/// <summary>Directory view of a user: identity fields plus the additive role set and linked login providers.</summary>
public record UserSummary(
    string Id,
    string DisplayName,
    string? Email,
    string? AvatarUrl,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> LoginProviders,
    DateTimeOffset CreatedAt);
