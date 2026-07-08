using System.Text;
using WestMarch.Application.Adventures;
using WestMarch.Application.Bestiary;
using WestMarch.Application.Common;
using WestMarch.Domain.Bestiary;
using WestMarch.Infrastructure.Bestiary;
using WestMarch.Infrastructure.Items;
using WestMarch.Infrastructure.Persistence.Repositories;

namespace WestMarch.Tests;

public class ChallengeRatingTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1/8", 0.125)]
    [InlineData("1/4", 0.25)]
    [InlineData("1/2", 0.5)]
    [InlineData("10", 10)]
    [InlineData("30", 30)]
    [InlineData("garbage", 0)]
    [InlineData(null, 0)]
    public void Cr_strings_parse_including_fractions(string? input, decimal expected) =>
        Assert.Equal(expected, ChallengeRatings.Parse(input));

    [Fact]
    public void Default_filter_keeps_dragons_out_of_starter_dungeons()
    {
        // A levels 3–4 adventure sees up to CR 6: bosses allowed, Adult Black Dragon (CR 14) not.
        var ceiling = ChallengeRatings.DefaultMaxCr(adventureMaxLevel: 4);
        Assert.Equal(6, ceiling);
        Assert.True(ChallengeRatings.Parse("1/2") <= ceiling);   // Orc pack: fine
        Assert.True(ChallengeRatings.Parse("5") <= ceiling);     // boss-tier: fine
        Assert.False(ChallengeRatings.Parse("14") <= ceiling);   // Adult Black Dragon: filtered
    }
}

/// <summary>Bestiary import + encounter authoring against the real services and database.</summary>
public class BestiaryTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    private IMonsterService Monsters() =>
        new MonsterService(new MonsterRepository(_t.Db), _t.Db, _t.CurrentUser);

    private IAdventureService Adventures() =>
        new AdventureService(new AdventureRepository(_t.Db), new TagRepository(_t.Db),
            new CatalogRepository(_t.Db), new MonsterRepository(_t.Db), _t.Db, _t.CurrentUser);

    private const string MonsterFileV1 = """
        [
          { "name": "Orc", "ac": 13, "size": "medium", "creatureType": "humanoid", "alignment": "chaotic evil",
            "maxHitPoints": 15, "hitDice": "2d8", "stats": { "str": 16, "dex": 12, "con": 16, "int": 7, "wis": 11, "cha": 10 },
            "traits": ["Aggressive. The orc can move toward a hostile creature as a bonus action."],
            "actions": { "list": ["Greataxe. +5 to hit, 9 (1d12+3) slashing."] },
            "challenge": { "rating": "1/2", "xp": 100 } },
          { "name": "Bugbear", "ac": 16, "size": "medium", "creatureType": "humanoid",
            "maxHitPoints": 27, "challenge": { "rating": "1", "xp": 200 } },
          { "name": "Adult Black Dragon", "ac": 19, "size": "huge", "creatureType": "dragon",
            "maxHitPoints": 195, "challenge": { "rating": "14", "xp": 11500 } }
        ]
        """;

    // V2: Orc buffed, Bugbear gone, Wolf new.
    private const string MonsterFileV2 = """
        [
          { "name": "Orc", "ac": 13, "size": "medium", "creatureType": "humanoid",
            "maxHitPoints": 20, "challenge": { "rating": "1/2", "xp": 100 } },
          { "name": "Adult Black Dragon", "ac": 19, "size": "huge", "creatureType": "dragon",
            "maxHitPoints": 195, "challenge": { "rating": "14", "xp": 11500 } },
          { "name": "Wolf", "ac": 13, "size": "medium", "creatureType": "beast",
            "maxHitPoints": 11, "challenge": { "rating": "1/4", "xp": 50 } }
        ]
        """;

    private static async Task<ParsedMonsterFile> ParseAsync(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await new MonsterJsonParser().ParseAsync(stream);
    }

    [Fact]
    public async Task Parser_reads_the_flat_monster_array_and_preserves_full_stats()
    {
        var parsed = await ParseAsync(MonsterFileV1);

        Assert.Equal(3, parsed.Monsters.Count);

        var orc = parsed.Monsters.Single(m => m.Name == "Orc");
        Assert.Equal(0.5m, orc.CrValue);
        Assert.Equal(13, orc.ArmorClass);
        Assert.Equal(15, orc.MaxHitPoints);
        Assert.Contains("Aggressive", orc.StatsJson); // full record kept for stat blocks
    }

    [Fact]
    public async Task Bestiary_import_upserts_and_retires_like_the_item_catalog()
    {
        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await Monsters().ApplyImportAsync(await ParseAsync(MonsterFileV1), "monsters-v1.json");

        var preview = await Monsters().PreviewImportAsync(await ParseAsync(MonsterFileV2));
        Assert.Single(preview.Added);        // Wolf
        Assert.Single(preview.Updated);      // Orc HP change
        Assert.Single(preview.Deactivated);  // Bugbear

        var batch = await Monsters().ApplyImportAsync(await ParseAsync(MonsterFileV2), "monsters-v2.json");
        Assert.Equal((1, 1, 1), (batch.AddedCount, batch.UpdatedCount, batch.DeactivatedCount));

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var all = await Monsters().ListAsync(new MonsterFilter(IncludeInactive: true));
        Assert.Equal(20, all.Single(m => m.Name == "Orc").MaxHitPoints);
        Assert.False(all.Single(m => m.Name == "Bugbear").IsActive); // retired, not deleted
    }

    [Fact]
    public async Task Bestiary_is_dm_gated_and_imports_are_ca_gated()
    {
        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Monsters().ListAsync(new MonsterFilter()));

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Monsters().ApplyImportAsync(new ParsedMonsterFile(null, [new ParsedMonster("X", "1", 1, null, 10, 5, null, "small", "beast", null, "{}")]), "x.json"));
    }

    [Fact]
    public async Task Cr_filter_trims_the_picker_list()
    {
        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await Monsters().ApplyImportAsync(await ParseAsync(MonsterFileV1), "monsters.json");

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var forStarters = await Monsters().ListAsync(new MonsterFilter(MaxCr: ChallengeRatings.DefaultMaxCr(4)));

        Assert.Contains(forStarters, m => m.Name == "Orc");
        Assert.DoesNotContain(forStarters, m => m.Name == "Adult Black Dragon");
    }

    [Fact]
    public async Task Encounters_round_trip_through_adventure_authoring()
    {
        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await Monsters().ApplyImportAsync(await ParseAsync(MonsterFileV1), "monsters.json");
        var orc = (await Monsters().ListAsync(new MonsterFilter(Search: "Orc"))).Single(m => m.Name == "Orc");

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var adventure = await Adventures().CreateAsync(new AdventureInput(
            "The Kennels", 3, 5, 4, 6, "s", "l", null,
            DateTimeOffset.Now.AddDays(-1), null, [], [], [],
            [new EncounterInput(
                "Room 2: The Kennels",
                "The orcs fight in the doorway.",
                "You hear snarling behind the splintered door.",
                [new EncounterNpcInput("Kennelmaster Grot", "AC 15, HP 30", "Cowardly; surrenders at half HP")],
                [new EncounterMonsterInput(orc.Id, 8)])]));

        var loaded = await Adventures().GetAsync(adventure.Id);
        var encounter = Assert.Single(loaded.Encounters);
        Assert.Equal("Room 2: The Kennels", encounter.Title);
        Assert.Equal("You hear snarling behind the splintered door.", encounter.ReadAloud);
        Assert.Equal("Kennelmaster Grot", Assert.Single(encounter.Npcs).Name);
        var em = Assert.Single(encounter.Monsters);
        Assert.Equal(8, em.Count);
        Assert.Equal("Orc", em.Monster.Name);

        // Update replaces the graph wholesale without duplicating rows.
        var updated = await Adventures().UpdateAsync(adventure.Id, new AdventureInput(
            "The Kennels", 3, 5, 4, 6, "s", "l", null,
            DateTimeOffset.Now.AddDays(-1), null, [], [], [],
            [new EncounterInput("Renamed room", null, null, [], [new EncounterMonsterInput(orc.Id, 2)])]));

        var after = await Adventures().GetAsync(adventure.Id);
        var only = Assert.Single(after.Encounters);
        Assert.Equal("Renamed room", only.Title);
        Assert.Equal(2, Assert.Single(only.Monsters).Count);
        Assert.Empty(only.Npcs);
    }

    [Fact]
    public async Task Encounter_validation_rejects_zero_counts_and_ghost_monsters()
    {
        _t.CurrentUser.BecomeDm(TestDb.DmId);

        await Assert.ThrowsAsync<AppValidationException>(() => Adventures().CreateAsync(new AdventureInput(
            "Bad Counts", 3, 5, 4, 6, "s", "l", null, DateTimeOffset.Now.AddDays(-1), null, [], [], [],
            [new EncounterInput("E", null, null, [], [new EncounterMonsterInput(Guid.NewGuid(), 0)])])));

        await Assert.ThrowsAsync<AppValidationException>(() => Adventures().CreateAsync(new AdventureInput(
            "Ghost Monster", 3, 5, 4, 6, "s", "l", null, DateTimeOffset.Now.AddDays(-1), null, [], [], [],
            [new EncounterInput("E", null, null, [], [new EncounterMonsterInput(Guid.NewGuid(), 3)])])));
    }

    [Fact]
    public async Task Players_never_see_encounters()
    {
        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await Monsters().ApplyImportAsync(await ParseAsync(MonsterFileV1), "monsters.json");
        var orc = (await Monsters().ListAsync(new MonsterFilter(Search: "Orc"))).Single(m => m.Name == "Orc");

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var adventure = await Adventures().CreateAsync(new AdventureInput(
            "Secret Rooms", 3, 5, 4, 6, "s", "l", "notes", DateTimeOffset.Now.AddDays(-1), null, [], [], [],
            [new EncounterInput("The trap", "spoilers", "boxed text", [], [new EncounterMonsterInput(orc.Id, 4)])]));
        await Adventures().SubmitForReviewAsync(adventure.Id);
        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await Adventures().ApproveAsync(adventure.Id);

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        var view = await Adventures().GetAsync(adventure.Id);
        Assert.Empty(view.Encounters);
        Assert.Null(view.DmNotes);
    }
}
