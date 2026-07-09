using Microsoft.Extensions.Logging.Abstractions;
using WestMarch.Application.Common;
using WestMarch.Infrastructure.Seeding;

namespace WestMarch.Tests;

/// <summary>
/// The demo reset is a destructive tool; these tests pin its two server-side guards.
/// (The wipe+reseed path itself is exercised manually — it depends on SQL Server
/// migrations and the reference files, which the unit database doesn't carry.)
/// </summary>
public class DemoResetTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    private IDemoResetService Service(bool demoMode) =>
        new DemoResetService(
            _t.Db,
            _t.CurrentUser,
            new DemoMode(demoMode),
            services: null!, // only reached after both guards pass; never in these tests
            NullLogger<DemoResetService>.Instance);

    [Fact]
    public async Task Reset_refuses_outside_demo_mode_even_for_a_ca()
    {
        _t.CurrentUser.BecomeCa(TestDb.CaId);

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => Service(demoMode: false).ResetAsync());
        Assert.Contains("demo mode", ex.Message);
    }

    [Fact]
    public async Task Reset_refuses_non_cas_even_in_demo_mode()
    {
        _t.CurrentUser.BecomeDm(TestDb.DmId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Service(demoMode: true).ResetAsync());

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Service(demoMode: true).ResetAsync());
    }
}
