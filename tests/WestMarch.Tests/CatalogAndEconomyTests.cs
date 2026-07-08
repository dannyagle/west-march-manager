using System.Text;
using WestMarch.Application.Adventures;
using WestMarch.Application.Characters;
using WestMarch.Application.Common;
using WestMarch.Application.Items;
using WestMarch.Application.Sessions;
using WestMarch.Domain.Items;
using WestMarch.Infrastructure.Items;
using WestMarch.Infrastructure.Persistence;
using WestMarch.Infrastructure.Persistence.Repositories;

namespace WestMarch.Tests;

/// <summary>
/// Phase 2: catalog import semantics, reward collection, and the marketplace —
/// including the promises that matter most: re-imports never clobber CA overrides
/// or custom items, rewards collect exactly once, and two buyers can't buy one listing.
/// </summary>
public class CatalogAndEconomyTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    // ---------- service factories (real services over the shared test database) ----------

    private ICatalogService CatalogSvc(AppDbContext? db = null, FakeCurrentUser? user = null) =>
        new CatalogService(new CatalogRepository(db ?? _t.Db), db ?? _t.Db, user ?? _t.CurrentUser);

    private IAdventureService AdventureSvc() =>
        new AdventureService(new AdventureRepository(_t.Db), new TagRepository(_t.Db),
            new CatalogRepository(_t.Db), new WestMarch.Infrastructure.Bestiary.MonsterRepository(_t.Db),
            _t.Db, _t.CurrentUser);

    private ICharacterService CharacterSvc() =>
        new CharacterService(new CharacterRepository(_t.Db), _t.Db, _t.CurrentUser);

    private ISessionService SessionSvc() =>
        new SessionService(new SessionRepository(_t.Db), new AdventureRepository(_t.Db),
            new CharacterRepository(_t.Db), _t.UserDirectory, _t.Broadcaster, _t.Db, _t.CurrentUser);

    private IRewardClaimService ClaimSvc() =>
        new RewardClaimService(new SessionRepository(_t.Db), new AdventureRepository(_t.Db),
            new CharacterRepository(_t.Db), new InventoryRepository(_t.Db), _t.Db, _t.CurrentUser);

    private IMarketplaceService MarketSvc(AppDbContext? db = null, FakeCurrentUser? user = null)
    {
        var context = db ?? _t.Db;
        return new MarketplaceService(new MarketRepository(context), new InventoryRepository(context),
            new CharacterRepository(context), context, user ?? _t.CurrentUser);
    }

    private IInventoryService InventorySvc() =>
        new InventoryService(new InventoryRepository(_t.Db), new CharacterRepository(_t.Db), _t.CurrentUser);

    // ---------- parser + import ----------

    private const string MagicFileV1 = """
        {
          "source": "test", "source_license": "CC",
          "items": [
            { "name": "Sun Blade", "rarity": "Rare", "type": "Weapon", "requires_attunement": true,
              "price": { "raw": "6000 GP", "gp": 6000, "base_item_plus": false, "special": null }, "url": "http://x/sun-blade" },
            { "name": "Belt of Giant Strength", "rarity": "Rare", "type": "Wondrous Item", "requires_attunement": true,
              "price": { "raw": "Varies", "gp": null, "base_item_plus": false, "special": "varies" }, "url": null, "multi_rarity": true },
            { "name": "Belt of Giant Strength", "rarity": "Legendary", "type": "Wondrous Item", "requires_attunement": true,
              "price": { "raw": "Varies", "gp": null, "base_item_plus": false, "special": "varies" }, "url": null, "multi_rarity": true },
            { "name": "Dust of Dryness", "rarity": "Uncommon", "type": "Wondrous Item", "requires_attunement": false,
              "price": { "raw": "120 GP", "gp": 120, "base_item_plus": false, "special": null }, "url": null }
          ]
        }
        """;

    // V2: Sun Blade price changed, Dust of Dryness gone, Ring of Warmth new.
    private const string MagicFileV2 = """
        {
          "source": "test", "source_license": "CC",
          "items": [
            { "name": "Sun Blade", "rarity": "Rare", "type": "Weapon", "requires_attunement": true,
              "price": { "raw": "6500 GP", "gp": 6500, "base_item_plus": false, "special": null }, "url": "http://x/sun-blade" },
            { "name": "Belt of Giant Strength", "rarity": "Rare", "type": "Wondrous Item", "requires_attunement": true,
              "price": { "raw": "Varies", "gp": null, "base_item_plus": false, "special": "varies" }, "url": null, "multi_rarity": true },
            { "name": "Belt of Giant Strength", "rarity": "Legendary", "type": "Wondrous Item", "requires_attunement": true,
              "price": { "raw": "Varies", "gp": null, "base_item_plus": false, "special": "varies" }, "url": null, "multi_rarity": true },
            { "name": "Ring of Warmth", "rarity": "Uncommon", "type": "Ring", "requires_attunement": true,
              "price": { "raw": "200 GP", "gp": 200, "base_item_plus": false, "special": null }, "url": null }
          ]
        }
        """;

    private static async Task<ParsedCatalogFile> ParseMagicAsync(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await new CatalogJsonParser().ParseAsync(stream, ItemKind.Magic);
    }

    [Fact]
    public async Task Parser_reads_the_magic_file_shape_including_multi_rarity_duplicates()
    {
        var parsed = await ParseMagicAsync(MagicFileV1);

        Assert.Equal(4, parsed.Items.Count);

        var belts = parsed.Items.Where(i => i.Name == "Belt of Giant Strength").ToList();
        Assert.Equal(2, belts.Count); // one row per rarity — distinct import keys
        Assert.NotEqual(belts[0].ImportKey, belts[1].ImportKey);
        Assert.All(belts, b => Assert.Null(b.BasePriceGp)); // "Varies" → no numeric price

        var sunBlade = parsed.Items.Single(i => i.Name == "Sun Blade");
        Assert.Equal(6000, sunBlade.BasePriceGp);
        Assert.True(sunBlade.RequiresAttunement);
    }

    [Fact]
    public async Task Only_a_ca_can_import_the_catalog()
    {
        var parsed = await ParseMagicAsync(MagicFileV1);

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => CatalogSvc().ApplyImportAsync(parsed, "magic.json"));
    }

    [Fact]
    public async Task Reimport_updates_base_fields_but_never_touches_overrides_or_customs()
    {
        _t.CurrentUser.BecomeCa(TestDb.CaId);
        var catalog = CatalogSvc();

        await catalog.ApplyImportAsync(await ParseMagicAsync(MagicFileV1), "magic-v1.json");

        // The CA prices a Varies item and forges a custom item.
        var beltRare = (await catalog.ListAsync(new CatalogFilter(Search: "Belt")))
            .Single(i => i.Rarity == ItemRarity.Rare);
        await catalog.UpdateAsync(beltRare.Id, new CatalogItemUpdate(6000, IsActive: true));

        var custom = await catalog.CreateCustomAsync(new CustomItemInput(
            ItemKind.Magic, "Mira's Homebrew Compass", ItemRarity.Uncommon, "Wondrous Item", false, 250, null));

        // A year later: refreshed file arrives.
        var preview = await catalog.PreviewImportAsync(await ParseMagicAsync(MagicFileV2));
        Assert.Single(preview.Added);                      // Ring of Warmth
        Assert.Single(preview.Updated);                    // Sun Blade price change
        Assert.Single(preview.Deactivated);                // Dust of Dryness
        Assert.Equal("Ring of Warmth", preview.Added[0].Name);

        var batch = await catalog.ApplyImportAsync(await ParseMagicAsync(MagicFileV2), "magic-v2.json");
        Assert.Equal((1, 1, 1), (batch.AddedCount, batch.UpdatedCount, batch.DeactivatedCount));

        var all = await catalog.ListAsync(new CatalogFilter(IncludeInactive: true));

        Assert.Equal(6500, all.Single(i => i.Name == "Sun Blade").BasePriceGp);          // updated
        Assert.False(all.Single(i => i.Name == "Dust of Dryness").IsActive);              // retired, not deleted
        Assert.Equal(6000, all.Single(i => i.Id == beltRare.Id).CampaignPriceGp);         // override survived
        Assert.NotNull(all.SingleOrDefault(i => i.Id == custom.Id));                      // custom untouched
        Assert.Equal(250, all.Single(i => i.Id == custom.Id).BasePriceGp);
    }

    // ---------- reward collection ----------

    private async Task<(Guid SessionId, Guid CharacterId, Guid CatalogItemId)> RunSessionToCompletionAsync()
    {
        // CA imports the catalog and prices the belt.
        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await CatalogSvc().ApplyImportAsync(await ParseMagicAsync(MagicFileV1), "magic.json");
        var sunBlade = (await CatalogSvc().ListAsync(new CatalogFilter(Search: "Sun Blade"))).Single();

        // DM authors an adventure: 100 gp guaranteed + choose-1-of-2 (catalog item or free text).
        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var adventure = await AdventureSvc().CreateAsync(new AdventureInput(
            "The Radiant Vault", 3, 5, 4, 6, "s", "l", null,
            DateTimeOffset.Now.AddDays(-1), null, [],
            [new RewardComponentInput(Domain.Adventures.RewardKind.Gold, 100, "100 gp bounty")],
            [new RewardOptionSetInput("Pick a prize",
            [
                new RewardOptionInput("", null, sunBlade.Id),
                new RewardOptionInput("A heartfelt letter", null),
            ])],
            []));
        await AdventureSvc().SubmitForReviewAsync(adventure.Id);
        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await AdventureSvc().ApproveAsync(adventure.Id);

        // Player joins with a fresh character; DM runs and completes the session.
        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        var character = await CharacterSvc().CreateAsync(
            new CharacterInput("Radiant Rae", null, "https://www.dndbeyond.com/characters/42"));

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        var session = await SessionSvc().CreateAsync(
            new SessionInput(adventure.Id, DateTimeOffset.Now.AddDays(1), null, VolunteerAsDm: true));

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        await SessionSvc().SignUpAsync(session.Id, character.Id);

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        await SessionSvc().CompleteAsync(session.Id, [character.Id]);

        return (session.Id, character.Id, sunBlade.Id);
    }

    [Fact]
    public async Task Collecting_rewards_grants_gold_and_mints_the_chosen_item_once_only()
    {
        var (sessionId, characterId, catalogItemId) = await RunSessionToCompletionAsync();

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        var states = await ClaimSvc().GetClaimStatesAsync(sessionId);
        var state = Assert.Single(states);
        Assert.False(state.AlreadyClaimed);
        Assert.Equal(100, state.GuaranteedGold);
        var set = Assert.Single(state.ChoiceSets);

        var itemOption = set.Options.Single(o => o.CatalogItemId == catalogItemId);
        await ClaimSvc().ClaimAsync(sessionId, characterId, new Dictionary<Guid, Guid> { [set.SetId] = itemOption.OptionId });

        var character = await CharacterSvc().GetAsync(characterId);
        Assert.Equal(100, character.GoldGp);

        var inventory = await InventorySvc().GetInventoryAsync(characterId);
        var owned = Assert.Single(inventory);
        Assert.Equal("Sun Blade", owned.CatalogItem.Name);

        var ledger = await InventorySvc().GetLedgerAsync(characterId);
        Assert.Contains(ledger, l => l.Type == LedgerEntryType.RewardGold && l.GoldDelta == 100);
        Assert.Contains(ledger, l => l.Type == LedgerEntryType.RewardItem && l.ItemName == "Sun Blade");

        // Once only.
        await Assert.ThrowsAsync<AppValidationException>(() =>
            ClaimSvc().ClaimAsync(sessionId, characterId, new Dictionary<Guid, Guid> { [set.SetId] = itemOption.OptionId }));
    }

    [Fact]
    public async Task Only_the_owner_can_collect_and_a_choice_is_required()
    {
        var (sessionId, characterId, _) = await RunSessionToCompletionAsync();

        _t.CurrentUser.BecomePlayer(TestDb.OtherPlayerId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            ClaimSvc().ClaimAsync(sessionId, characterId, new Dictionary<Guid, Guid>()));

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        await Assert.ThrowsAsync<AppValidationException>(() =>
            ClaimSvc().ClaimAsync(sessionId, characterId, new Dictionary<Guid, Guid>())); // no pick
    }

    // ---------- marketplace ----------

    private async Task<(Guid InstanceId, Guid SellerCharacterId)> OwnedSunBladeAsync()
    {
        var (sessionId, characterId, catalogItemId) = await RunSessionToCompletionAsync();

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        var state = (await ClaimSvc().GetClaimStatesAsync(sessionId)).Single();
        var set = state.ChoiceSets.Single();
        var option = set.Options.Single(o => o.CatalogItemId == catalogItemId);
        await ClaimSvc().ClaimAsync(sessionId, characterId, new Dictionary<Guid, Guid> { [set.SetId] = option.OptionId });

        var instance = (await InventorySvc().GetInventoryAsync(characterId)).Single();
        return (instance.Id, characterId);
    }

    [Fact]
    public async Task Quick_sell_pays_half_value_and_retires_the_item()
    {
        var (instanceId, sellerId) = await OwnedSunBladeAsync();

        var proceeds = await MarketSvc().QuickSellAsync(instanceId);
        Assert.Equal(3000, proceeds); // half of 6000

        var character = await CharacterSvc().GetAsync(sellerId);
        Assert.Equal(3100, character.GoldGp); // 100 reward + 3000

        Assert.Empty(await InventorySvc().GetInventoryAsync(sellerId)); // gone forever
    }

    [Fact]
    public async Task Buying_a_listing_moves_gold_and_the_item_between_characters()
    {
        var (instanceId, sellerId) = await OwnedSunBladeAsync();

        await MarketSvc().ListForSaleAsync(instanceId, 5000);

        // Another player's character with a fat purse.
        _t.CurrentUser.BecomePlayer(TestDb.OtherPlayerId);
        var buyer = await CharacterSvc().CreateAsync(new CharacterInput("Rich Rick", null, "https://www.dndbeyond.com/characters/77"));
        (await _t.Db.Characters.FindAsync(buyer.Id))!.GoldGp = 6000;
        await _t.Db.SaveChangesAsync();

        var listing = (await MarketSvc().BrowseAsync()).Single().Listing;

        // Not enough gold? No — 6000 ≥ 5000. But their own listing is out of reach:
        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        await Assert.ThrowsAsync<AppValidationException>(() => MarketSvc().BuyAsync(listing.Id, sellerId));

        _t.CurrentUser.BecomePlayer(TestDb.OtherPlayerId);
        await MarketSvc().BuyAsync(listing.Id, buyer.Id);

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        var seller = await CharacterSvc().GetAsync(sellerId);
        Assert.Equal(5100, seller.GoldGp); // 100 reward + 5000 sale

        _t.CurrentUser.BecomePlayer(TestDb.OtherPlayerId);
        var buyerAfter = await CharacterSvc().GetAsync(buyer.Id);
        Assert.Equal(1000, buyerAfter.GoldGp);

        var bought = Assert.Single(await InventorySvc().GetInventoryAsync(buyer.Id));
        Assert.Equal("Sun Blade", bought.CatalogItem.Name);

        // Both sides hit the ledger.
        Assert.Contains(await InventorySvc().GetLedgerAsync(buyer.Id),
            l => l.Type == LedgerEntryType.Purchase && l.GoldDelta == -5000);
        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        Assert.Contains(await InventorySvc().GetLedgerAsync(sellerId),
            l => l.Type == LedgerEntryType.SaleProceeds && l.GoldDelta == 5000);
    }

    [Fact]
    public async Task Two_buyers_one_listing_the_second_loses_cleanly()
    {
        var (instanceId, _) = await OwnedSunBladeAsync();
        await MarketSvc().ListForSaleAsync(instanceId, 100);
        var listingId = (await MarketSvc().BrowseAsync()).Single().Listing.Id;

        // Two buyers on separate contexts (separate circuits in production).
        _t.CurrentUser.BecomePlayer(TestDb.OtherPlayerId);
        var buyerA = await CharacterSvc().CreateAsync(new CharacterInput("First", null, "https://www.dndbeyond.com/characters/1"));
        (await _t.Db.Characters.FindAsync(buyerA.Id))!.GoldGp = 500;
        var userB = new FakeCurrentUser();
        userB.BecomePlayer(TestDb.OtherDmId);
        using var dbB = _t.NewContext();
        var buyerB = new Domain.Characters.Character
        {
            OwnerUserId = TestDb.OtherDmId, Name = "Second", DdbUrl = "https://www.dndbeyond.com/characters/2", GoldGp = 500,
        };
        dbB.Characters.Add(buyerB);
        await dbB.SaveChangesAsync();
        await _t.Db.SaveChangesAsync();

        var marketB = MarketSvc(dbB, userB);
        // B loads the listing into its context first (both see it Active)…
        _ = await marketB.GetPurchaseWarningsAsync(listingId, buyerB.Id);

        // …then A completes the purchase…
        await MarketSvc().BuyAsync(listingId, buyerA.Id);

        // …and B's attempt fails with a friendly error, not a double sale.
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => marketB.BuyAsync(listingId, buyerB.Id));
        Assert.Contains("someone else", ex.Message);

        _t.CurrentUser.BecomePlayer(TestDb.OtherPlayerId);
        Assert.Single(await InventorySvc().GetInventoryAsync(buyerA.Id));
    }

    [Fact]
    public async Task Unpriced_items_cannot_be_sold_until_a_ca_prices_them()
    {
        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await CatalogSvc().ApplyImportAsync(await ParseMagicAsync(MagicFileV1), "magic.json");
        var belt = (await CatalogSvc().ListAsync(new CatalogFilter(Search: "Belt")))
            .Single(i => i.Rarity == ItemRarity.Rare); // price: Varies

        // Hand the belt to a character directly (as if rewarded).
        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        var character = await CharacterSvc().CreateAsync(new CharacterInput("Belted", null, "https://www.dndbeyond.com/characters/9"));
        var instance = new ItemInstance { CatalogItemId = belt.Id, OwnerCharacterId = character.Id };
        _t.Db.ItemInstances.Add(instance);
        await _t.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => MarketSvc().QuickSellAsync(instance.Id));
        Assert.Contains("campaign price", ex.Message);

        // A CA prices it; now it sells for half.
        _t.CurrentUser.BecomeCa(TestDb.CaId);
        await CatalogSvc().UpdateAsync(belt.Id, new CatalogItemUpdate(6000, IsActive: true));

        _t.CurrentUser.BecomePlayer(TestDb.PlayerId);
        Assert.Equal(3000, await MarketSvc().QuickSellAsync(instance.Id));
    }

    [Fact]
    public async Task The_audit_ledger_is_ca_only()
    {
        var audit = new AuditService(new InventoryRepository(_t.Db), new CharacterRepository(_t.Db), _t.CurrentUser);

        _t.CurrentUser.BecomeDm(TestDb.DmId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => audit.ListAsync(new AuditFilter()));

        _t.CurrentUser.BecomeCa(TestDb.CaId);
        Assert.Empty(await audit.ListAsync(new AuditFilter()));
    }
}
