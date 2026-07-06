using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WestMarch.Application.Common;
using WestMarch.Application.Sessions;
using WestMarch.Application.Users;
using WestMarch.Infrastructure.Identity;
using WestMarch.Infrastructure.Persistence;

namespace WestMarch.Tests;

/// <summary>Mutable ICurrentUser so a test can act as different callers against the same data.</summary>
public class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; }
    public string? UserId { get; set; }
    public string? DisplayName { get; set; }
    public bool IsDmRole { get; set; }
    public bool IsCa { get; set; }

    public bool IsDm => IsDmRole || IsCa;

    public void BecomePlayer(string id) => Set(id, dm: false, ca: false);
    public void BecomeDm(string id) => Set(id, dm: true, ca: false);
    public void BecomeCa(string id) => Set(id, dm: false, ca: true);

    private void Set(string id, bool dm, bool ca)
    {
        IsAuthenticated = true;
        UserId = id;
        DisplayName = id;
        IsDmRole = dm;
        IsCa = ca;
    }
}

public class FakeUserDirectory : IUserDirectory
{
    public Task<IReadOnlyList<UserSummary>> SearchAsync(string? query, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UserSummary>>([]);

    public Task<UserSummary?> GetAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<UserSummary?>(null);

    public Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(userIds.Distinct().ToDictionary(id => id, id => id));

    public List<(string UserId, string Role)> Granted { get; } = [];

    public Task AddRoleAsync(string userId, string role, CancellationToken ct = default)
    {
        Granted.Add((userId, role));
        return Task.CompletedTask;
    }

    public Task RemoveRoleAsync(string userId, string role, CancellationToken ct = default)
    {
        Granted.Remove((userId, role));
        return Task.CompletedTask;
    }
}

public class FakeBroadcaster : ISessionMessageBroadcaster
{
    public List<BoardMessage> Sent { get; } = [];

    public Task BroadcastAsync(BoardMessage message, CancellationToken ct = default)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>SQLite-in-memory database with a few seeded users, plus the fakes services need.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Db { get; }
    public FakeCurrentUser CurrentUser { get; } = new();
    public FakeUserDirectory UserDirectory { get; } = new();
    public FakeBroadcaster Broadcaster { get; } = new();

    public const string PlayerId = "user-player";
    public const string OtherPlayerId = "user-other";
    public const string DmId = "user-dm";
    public const string OtherDmId = "user-dm2";
    public const string CaId = "user-ca";

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        Db.Database.EnsureCreated();

        foreach (var id in new[] { PlayerId, OtherPlayerId, DmId, OtherDmId, CaId })
        {
            Db.Users.Add(new ApplicationUser
            {
                Id = id,
                UserName = $"{id}@test.local",
                DisplayName = id,
            });
        }

        Db.SaveChanges();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
