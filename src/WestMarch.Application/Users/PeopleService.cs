using WestMarch.Application.Common;
using WestMarch.Domain.Users;

namespace WestMarch.Application.Users;

public interface IPeopleService
{
    Task<IReadOnlyList<UserSummary>> SearchAsync(string? query, CancellationToken ct = default);
    Task GrantRoleAsync(string userId, string role, CancellationToken ct = default);
    Task RevokeRoleAsync(string userId, string role, CancellationToken ct = default);
}

/// <summary>CA-only people manager: view users and adjust the additive role set.</summary>
public class PeopleService(IUserDirectory directory, ICurrentUser currentUser) : IPeopleService
{
    public Task<IReadOnlyList<UserSummary>> SearchAsync(string? query, CancellationToken ct = default)
    {
        RequireCa();
        return directory.SearchAsync(query, ct);
    }

    public Task GrantRoleAsync(string userId, string role, CancellationToken ct = default)
    {
        RequireCa();
        ValidateRole(role);
        return directory.AddRoleAsync(userId, role, ct);
    }

    public async Task RevokeRoleAsync(string userId, string role, CancellationToken ct = default)
    {
        RequireCa();
        ValidateRole(role);

        if (role == Roles.CampaignAdmin && userId == currentUser.UserId)
        {
            throw new AppValidationException("You cannot revoke your own Campaign Admin role.");
        }

        await directory.RemoveRoleAsync(userId, role, ct);
    }

    private void RequireCa()
    {
        if (!currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Campaign Admin role required.");
        }
    }

    private static void ValidateRole(string role)
    {
        if (!Roles.All.Contains(role))
        {
            throw new AppValidationException($"Unknown role '{role}'.");
        }
    }
}
