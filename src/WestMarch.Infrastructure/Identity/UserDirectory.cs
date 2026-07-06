using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WestMarch.Application.Common;
using WestMarch.Application.Users;
using WestMarch.Infrastructure.Persistence;

namespace WestMarch.Infrastructure.Identity;

/// <summary>IUserDirectory over ASP.NET Core Identity. Set-based queries; no N+1 per user.</summary>
public class UserDirectory(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager) : IUserDirectory
{
    public async Task<IReadOnlyList<UserSummary>> SearchAsync(string? query, CancellationToken ct = default)
    {
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            users = users.Where(u => u.DisplayName.Contains(q) || (u.Email != null && u.Email.Contains(q)));
        }

        var page = await users.OrderBy(u => u.DisplayName).Take(500).ToListAsync(ct);
        var ids = page.Select(u => u.Id).ToList();

        var roleRows = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where ids.Contains(ur.UserId)
            select new { ur.UserId, r.Name }).ToListAsync(ct);

        var loginRows = await db.UserLogins
            .Where(l => ids.Contains(l.UserId))
            .Select(l => new { l.UserId, l.LoginProvider })
            .ToListAsync(ct);

        var passwordUsers = page.Where(u => u.PasswordHash != null).Select(u => u.Id).ToHashSet();

        return [.. page.Select(u => new UserSummary(
            u.Id,
            u.DisplayName,
            u.Email,
            u.AvatarUrl,
            [.. roleRows.Where(r => r.UserId == u.Id).Select(r => r.Name!)],
            [.. loginRows.Where(l => l.UserId == u.Id).Select(l => l.LoginProvider)
                .Concat(passwordUsers.Contains(u.Id) ? ["Local password"] : Array.Empty<string>())],
            u.CreatedAt))];
    }

    public async Task<UserSummary?> GetAsync(string userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        var logins = await userManager.GetLoginsAsync(user);

        return new UserSummary(
            user.Id, user.DisplayName, user.Email, user.AvatarUrl,
            [.. roles],
            [.. logins.Select(l => l.LoginProvider)],
            user.CreatedAt);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    public async Task AddRoleAsync(string userId, string role, CancellationToken ct = default)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }

        var user = await userManager.FindByIdAsync(userId)
            ?? throw new NotFoundException("User", userId);

        var result = await userManager.AddToRoleAsync(user, role);
        ThrowIfFailed(result);
    }

    public async Task RemoveRoleAsync(string userId, string role, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new NotFoundException("User", userId);

        var result = await userManager.RemoveFromRoleAsync(user, role);
        ThrowIfFailed(result);
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new AppValidationException([.. result.Errors.Select(e => e.Description)]);
        }
    }
}
