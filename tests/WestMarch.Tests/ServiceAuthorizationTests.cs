using WestMarch.Application.Adventures;
using WestMarch.Application.Characters;
using WestMarch.Application.Common;
using WestMarch.Application.Sessions;
using WestMarch.Application.Users;
using WestMarch.Domain.Adventures;
using WestMarch.Domain.Users;
using WestMarch.Infrastructure.Persistence.Repositories;

namespace WestMarch.Tests;

/// <summary>
/// Authorization is enforced in the service layer, not just the UI.
/// These tests drive real services over a real (SQLite in-memory) database.
/// </summary>
public class ServiceAuthorizationTests : IDisposable
{
    private readonly TestDb _t = new();

    private ICharacterService Characters() =>
        new CharacterService(new CharacterRepository(_t.Db), _t.Db, _t.CurrentUser);

    private IAdventureService Adventures() =>
        new AdventureService(
            new AdventureRepository(_t.Db),
            new TagRepository(_t.Db),
            new WestMarch.Infrastructure.Items.CatalogRepository(_t.Db),
            _t.Db,
            _t.CurrentUser);

    private ISessionService Sessions() =>
        new SessionService(
            new SessionRepository(_t.Db),
            new AdventureRepository(_t.Db),
            new CharacterRepository(_t.Db),
            _t.UserDirectory,
            _t.Broadcaster,
            _t.Db,
            _t.CurrentUser);

    private IPeopleService People() => new PeopleService(_t.UserDirectory, _t.CurrentUser);

    private static readonly CharacterInput ValidCharacter =
        new("Bramblefoot", "Halfling Rogue", "https://www.dndbeyond.com/characters/12345678");

    private static AdventureInput ValidAdventure(string title = "The Goblin Watchtower") => new(
        title, 3, 5, 4, 6,
        "Short teaser.", "Long description.",
        "Secret DM notes.", "Goblin ×8",
        DateTimeOffset.Now.AddDays(-1), null,
        ["wilderness"], [], []);

    public void Dispose() => _t.Dispose();

    // ---------- characters ----------

    [Fact]
    public async Task Unauthenticated_callers_cannot_create_characters()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Characters().CreateAsync(ValidCharacter));
    }

    [Fact]
    public async Task Characters_require_a_valid_ddb_link_and_start_at_level_3()
    {
        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Characters().CreateAsync(ValidCharacter with { DdbUrl = "https://example.com/not-ddb" }));

        var created = await Characters().CreateAsync(ValidCharacter);
        Assert.Equal(3, created.Level);
        Assert.Equal(12345678, created.DdbCharacterId);
    }

    [Fact]
    public async Task Only_the_owner_or_a_ca_can_modify_a_character()
    {
        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        var character = await Characters().CreateAsync(ValidCharacter);

        _t.CurrentUser.BecomePlayer(TestDb.OtherPlayerId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Characters().UpdateAsync(character.Id, ValidCharacter with { Name = "Stolen" }));

        _t.CurrentUser.BecomeCa(TestDb.CaId);
        var renamed = await Characters().UpdateAsync(character.Id, ValidCharacter with { Name = "Renamed by CA" });
        Assert.Equal("Renamed by CA", renamed.Name);
    }

    // ---------- adventures: lifecycle + visibility ----------

    [Fact]
    public async Task Players_cannot_author_adventures()
    {
        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Adventures().CreateAsync(ValidAdventure()));
    }

    [Fact]
    public async Task Drafts_are_invisible_to_other_dms_until_ready_for_review()
    {
        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var draft = await Adventures().CreateAsync(ValidAdventure());

        _t.CurrentUser.BecomeDm(TestDb.OtherDmId);
        await Assert.ThrowsAsync<NotFoundException>(() => Adventures().GetAsync(draft.Id));

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        await Adventures().SubmitForReviewAsync(draft.Id);

        _t.CurrentUser.BecomeDm(TestDb.OtherDmId);
        var visible = await Adventures().GetAsync(draft.Id);
        Assert.Equal(AdventureStatus.ReadyForReview, visible.Status);
    }

    [Fact]
    public async Task Only_a_ca_can_approve_and_only_from_ready_for_review()
    {
        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var adventure = await Adventures().CreateAsync(ValidAdventure());

        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await Assert.ThrowsAsync<AppValidationException>(() => Adventures().ApproveAsync(adventure.Id)); // still draft

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        await Adventures().SubmitForReviewAsync(adventure.Id);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Adventures().ApproveAsync(adventure.Id)); // DM, not CA

        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await Adventures().ApproveAsync(adventure.Id);

        var approved = await Adventures().GetAsync(adventure.Id);
        Assert.Equal(AdventureStatus.Approved, approved.Status);
        Assert.Equal(TestDb.CaId, approved.ApprovedByUserId);
    }

    [Fact]
    public async Task Dm_only_fields_are_stripped_for_players()
    {
        var adventureId = await CreateApprovedAdventureAsync();

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        var view = await Adventures().GetAsync(adventureId);

        Assert.Null(view.DmNotes);
        Assert.Null(view.MonsterStatBlocks);
        Assert.Equal("Short teaser.", view.ShortDescription);
    }

    // ---------- sessions ----------

    [Fact]
    public async Task Sessions_can_only_schedule_approved_adventures()
    {
        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var draft = await Adventures().CreateAsync(ValidAdventure());

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        await Assert.ThrowsAsync<AppValidationException>(() =>
            Sessions().CreateAsync(new SessionInput(draft.Id, DateTimeOffset.Now.AddDays(3), null, false)));
    }

    [Fact]
    public async Task Players_can_create_sessions_but_cannot_take_the_dm_seat()
    {
        var adventureId = await CreateApprovedAdventureAsync();

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Sessions().CreateAsync(new SessionInput(adventureId, DateTimeOffset.Now.AddDays(3), null, VolunteerAsDm: true)));

        var session = await Sessions().CreateAsync(new SessionInput(adventureId, DateTimeOffset.Now.AddDays(3), null, false));
        Assert.Null(session.DmUserId);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Sessions().ClaimDmSeatAsync(session.Id));

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        await Sessions().ClaimDmSeatAsync(session.Id);
        Assert.Equal(TestDb.DmId, (await Sessions().GetAsync(session.Id)).DmUserId);
    }

    [Fact]
    public async Task Completing_a_session_awards_credits_and_levels_characters()
    {
        var adventureId = await CreateApprovedAdventureAsync();

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        var character = await Characters().CreateAsync(ValidCharacter);

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var session = await Sessions().CreateAsync(new SessionInput(adventureId, DateTimeOffset.Now.AddDays(1), null, VolunteerAsDm: true));

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        await Sessions().SignUpAsync(session.Id, character.Id);

        // A stranger (even the creator-player) cannot complete; the DM can.
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Sessions().CompleteAsync(session.Id, [character.Id]));

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        await Sessions().CompleteAsync(session.Id, [character.Id]);

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        var after = await Characters().GetAsync(character.Id);
        Assert.Equal(3, after.Level);
        Assert.Equal(1, after.CreditsAtCurrentLevel); // 1 of 2 toward level 4
        Assert.Single(after.Credits);
        Assert.False(after.Credits[0].TriggeredLevelUp);
    }

    [Fact]
    public async Task Posting_on_a_board_requires_participation_and_broadcasts()
    {
        var adventureId = await CreateApprovedAdventureAsync();

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var session = await Sessions().CreateAsync(new SessionInput(adventureId, DateTimeOffset.Now.AddDays(1), null, VolunteerAsDm: true));

        _t.CurrentUser.BecomePlayer(TestDb.OtherPlayerId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Sessions().PostMessageAsync(session.Id, "let me in"));

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var posted = await Sessions().PostMessageAsync(session.Id, "Muster at dusk.");

        Assert.Single(_t.Broadcaster.Sent);
        Assert.Equal(posted.Id, _t.Broadcaster.Sent[0].Id);
        Assert.Single(await Sessions().GetMessagesAsync(session.Id));
    }

    // ---------- people manager ----------

    [Fact]
    public async Task Only_cas_manage_roles_and_cannot_demote_themselves()
    {
        _t.CurrentUser.BecomeDm(TestDb.DmId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            People().GrantRoleAsync(TestDb.PlayerId, Roles.DungeonMaster));

        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await People().GrantRoleAsync(TestDb.PlayerId, Roles.DungeonMaster);
        Assert.Contains((TestDb.PlayerId, Roles.DungeonMaster), _t.UserDirectory.Granted);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            People().RevokeRoleAsync(TestDb.CaId, Roles.CampaignAdmin));

        await Assert.ThrowsAsync<AppValidationException>(() =>
            People().GrantRoleAsync(TestDb.PlayerId, "SuperUser"));
    }

    private async Task<Guid> CreateApprovedAdventureAsync()
    {
        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var adventure = await Adventures().CreateAsync(ValidAdventure());
        await Adventures().SubmitForReviewAsync(adventure.Id);

        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await Adventures().ApproveAsync(adventure.Id);
        return adventure.Id;
    }
}
