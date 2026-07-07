using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WestMarch.Application.Items;
using WestMarch.Domain.Adventures;
using WestMarch.Domain.Announcements;
using WestMarch.Domain.Characters;
using WestMarch.Domain.Items;
using WestMarch.Domain.Sessions;
using WestMarch.Domain.Users;
using WestMarch.Infrastructure.Identity;
using WestMarch.Infrastructure.Persistence;

namespace WestMarch.Infrastructure.Seeding;

/// <summary>
/// Development seed: applies migrations, ensures roles, and populates a believable
/// campaign so every screen has something to show. Idempotent — runs only into an empty DB.
/// All local accounts use the password "Passw0rd!".
/// </summary>
public static class DevDataSeeder
{
    public const string DevPassword = "Passw0rd!";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        await db.Database.MigrateAsync();

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // The catalog seeds whenever it is empty — even into an existing database —
        // so a Phase 1 dev DB picks up the item files without a wipe.
        var parser = scope.ServiceProvider.GetRequiredService<ICatalogFileParser>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        await SeedCatalogIfEmptyAsync(db, parser, config, env, logger);

        if (await db.Users.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding development data…");

        async Task<ApplicationUser> AddUser(string email, string displayName, params string[] roles)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName,
            };
            await userManager.CreateAsync(user, DevPassword);
            if (roles.Length > 0)
            {
                await userManager.AddToRolesAsync(user, roles);
            }
            return user;
        }

        var admin = await AddUser("admin@westmarch.local", "Mira the Cartographer", Roles.CampaignAdmin, Roles.DungeonMaster);
        var dmGareth = await AddUser("dm@westmarch.local", "Gareth Ironquill", Roles.DungeonMaster);
        var dmSable = await AddUser("dm2@westmarch.local", "Sable", Roles.DungeonMaster);
        var pOwen = await AddUser("player1@westmarch.local", "Owen Underbough");
        var pKira = await AddUser("player2@westmarch.local", "Kira Dawnwhisper");
        var pTobin = await AddUser("player3@westmarch.local", "Tobin Blackwater");
        var pElsa = await AddUser("player4@westmarch.local", "Elsariel");

        Character MakeCharacter(ApplicationUser owner, string name, string summary, int level, int credits) => new()
        {
            OwnerUserId = owner.Id,
            Name = name,
            Summary = summary,
            Level = level,
            CreditsAtCurrentLevel = credits,
            DdbUrl = "https://www.dndbeyond.com/characters/12345678",
            DdbCharacterId = 12345678,
        };

        var chars = new[]
        {
            MakeCharacter(pOwen, "Bramblefoot", "Halfling Rogue", 3, 1),
            MakeCharacter(pOwen, "Sir Percival", "Human Paladin", 4, 0),
            MakeCharacter(pKira, "Kirael", "Elf Druid", 3, 0),
            MakeCharacter(pTobin, "Grimjaw", "Half-orc Barbarian", 5, 1),
            MakeCharacter(pElsa, "Elsariel Moonshadow", "Elf Wizard", 11, 1),
            MakeCharacter(admin, "Thornwick", "Gnome Artificer", 3, 0),
        };
        db.Characters.AddRange(chars);

        var tags = new Dictionary<string, Tag>();
        Tag T(string name) => tags.TryGetValue(name, out var t) ? t : tags[name] = new Tag { Name = name };

        var now = DateTimeOffset.Now;

        // Catalog lookups for linking demo rewards to real items (defensive: file may change).
        async Task<CatalogItem?> FindItem(string name) =>
            await db.CatalogItems.FirstOrDefaultAsync(c => c.Name == name && c.Kind == ItemKind.Magic);

        var cloak = await FindItem("Cloak of Billowing");
        var marinersArmor = await FindItem("Mariner's Armor");
        var wandOfSecrets = await FindItem("Wand of Secrets");
        var staffOfWithering = await FindItem("Staff of Withering");

        var goblinWatch = new Adventure
        {
            Title = "The Goblin Watchtower",
            AuthorUserId = dmGareth.Id,
            MinLevel = 3,
            MaxLevel = 5,
            ShortDescription = "Smoke rises from the old border watchtower. The goblins of the Bent Fang have claimed it — and something is organizing them.",
            LongDescription = "The watchtower on the Elderline has stood empty since the retreat. Now travellers report drums at dusk and green fires on its crown. The Wardens will pay for its recapture, and pay better for whoever — or whatever — taught the Bent Fang discipline.",
            DmNotes = "The 'organizer' is a hobgoblin exile, Vex. She will parley if cornered. The tower basement hides a Warden supply cache (map to The Sunken Vault).",
            MonsterStatBlocks = "8× Goblin (MM p.166)\n2× Goblin Boss (MM p.166)\n1× Hobgoblin Captain 'Vex' (MM p.186) — AC 17, HP 39, parley DC 14",
            Status = AdventureStatus.Approved,
            ApprovedByUserId = admin.Id,
            ApprovedAt = now.AddDays(-20),
            ActiveFrom = now.AddDays(-30),
            Tags = [T("wilderness"), T("classic")],
            GuaranteedRewards =
            [
                new RewardComponent { Kind = RewardKind.Gold, GoldAmount = 150, Description = "150 gp Warden bounty", SortOrder = 0 },
            ],
            RewardOptionSets =
            [
                new RewardOptionSet
                {
                    Name = "Spoils of the tower",
                    SortOrder = 0,
                    Options =
                    [
                        new RewardOption { Description = "Cloak of Billowing", CatalogItemId = cloak?.Id, SortOrder = 0 },
                        new RewardOption { Description = "Potion of Hill Giant Strength", SortOrder = 1 },
                        new RewardOption { Description = "Vex's map satchel (three unexplored hex leads)", SortOrder = 2 },
                    ],
                },
            ],
        };

        var sunkenVault = new Adventure
        {
            Title = "The Sunken Vault of Aldremir",
            AuthorUserId = dmGareth.Id,
            MinLevel = 4,
            MaxLevel = 6,
            ShortDescription = "A drowned dwarven vault has surfaced in the fen. Its doors are open. They were not opened from outside.",
            LongDescription = "When the marsh receded after the storm, the vault's spire broke the waterline for the first time in two centuries. The Cartographers' Guild wants its ledgers; the vault wants visitors. Bring rope, bring light, and mind the water level.",
            DmNotes = "Water rises one 'step' every 30 real minutes — track it visibly. The ledgers implicate a founding family of the town.",
            MonsterStatBlocks = "4× Ghoul (MM p.148)\n1× Water Weird (MM p.299)\n1× Flameskull 'the Archivist' (MM p.134) — will trade answers for fire",
            Status = AdventureStatus.ReadyForReview,
            ActiveFrom = now.AddDays(-5),
            Tags = [T("dungeon"), T("horror")],
            GuaranteedRewards =
            [
                new RewardComponent { Kind = RewardKind.Gold, GoldAmount = 250, Description = "250 gp in vault coinage", SortOrder = 0 },
                new RewardComponent { Kind = RewardKind.Other, GoldAmount = null, Description = "Guild favor: one free identify per character", SortOrder = 1 },
            ],
            RewardOptionSets =
            [
                new RewardOptionSet
                {
                    Name = "Choose one relic",
                    SortOrder = 0,
                    Options =
                    [
                        new RewardOption { Description = "Mariner's Armor", CatalogItemId = marinersArmor?.Id, SortOrder = 0 },
                        new RewardOption { Description = "Wand of Secrets", CatalogItemId = wandOfSecrets?.Id, SortOrder = 1 },
                    ],
                },
            ],
        };

        var kingInAmber = new Adventure
        {
            Title = "The King in Amber",
            AuthorUserId = admin.Id,
            MinLevel = 9,
            MaxLevel = 12,
            ShortDescription = "Deep in the Elderwood, woodcutters found a hall swallowed by amber — and a crowned figure inside it, watching them.",
            LongDescription = "An epic-tier expedition into the Elderwood's oldest grove. The amber hall predates every map the Guild holds. Whatever royal court was frozen there, its heraldry matches nothing in the archives — except one seal in the town founder's crypt.",
            DmNotes = "Part 1 of the 'Amber Court' epic. The king wakes if the amber is damaged. He speaks only in questions.",
            MonsterStatBlocks = "1× Treant (MM p.289)\n6× Blight assortment\n'The King in Amber' — use Archmage chassis (MM p.342), lair actions: amber grasp (DC 16 STR).",
            Status = AdventureStatus.Approved,
            ApprovedByUserId = admin.Id,
            ApprovedAt = now.AddDays(-7),
            ActiveFrom = now.AddDays(-7),
            ActiveUntil = now.AddDays(60),
            Tags = [T("epic event"), T("mystery"), T("wilderness")],
            GuaranteedRewards =
            [
                new RewardComponent { Kind = RewardKind.Gold, GoldAmount = 900, Description = "900 gp: Guild expedition purse", SortOrder = 0 },
            ],
            RewardOptionSets =
            [
                new RewardOptionSet
                {
                    Name = "Boon of the Amber Court",
                    SortOrder = 0,
                    Options =
                    [
                        new RewardOption { Description = "Staff of Withering", CatalogItemId = staffOfWithering?.Id, SortOrder = 0 },
                        new RewardOption { Description = "Amber shard: once, ask the King one question (DM adjudicates)", SortOrder = 1 },
                        new RewardOption { Description = "Ring of Resistance (poison)", ExternalUrl = "http://dnd2024.wikidot.com/wondrous-items:ring-of-resistance", SortOrder = 2 },
                    ],
                },
            ],
        };

        var draftIdea = new Adventure
        {
            Title = "Rats in the Granary (draft)",
            AuthorUserId = dmSable.Id,
            MinLevel = 3,
            MaxLevel = 4,
            ShortDescription = "The harvest stores are vanishing overnight. The miller blames rats. The rats, if asked, blame the miller.",
            LongDescription = "Work in progress — a one-night mystery for fresh level-3 characters.",
            DmNotes = "TODO: stat the wererat, decide who's lying.",
            Status = AdventureStatus.Draft,
            ActiveFrom = now,
            Tags = [T("mystery"), T("town")],
        };

        db.Adventures.AddRange(goblinWatch, sunkenVault, kingInAmber, draftIdea);

        DateTimeOffset NextEvening(int daysOut) =>
            new(DateOnly.FromDateTime(now.LocalDateTime.Date.AddDays(daysOut)), new TimeOnly(18, 30), now.Offset);

        var s1 = new GameSession
        {
            Adventure = goblinWatch,
            ScheduledAt = NextEvening(3),
            DmUserId = dmGareth.Id,
            CreatedByUserId = dmGareth.Id,
            Location = "The Dragon's Hoard — back table",
            Signups =
            [
                new SessionSignup { Character = chars[0] },
                new SessionSignup { Character = chars[2] },
            ],
        };

        var s2 = new GameSession
        {
            Adventure = kingInAmber,
            ScheduledAt = NextEvening(6),
            DmUserId = null, // needs a DM — shows on the DM board
            CreatedByUserId = pElsa.Id,
            Location = "The Dragon's Hoard — main hall",
            Signups = [new SessionSignup { Character = chars[4] }],
        };

        var s3 = new GameSession
        {
            Adventure = goblinWatch,
            ScheduledAt = NextEvening(10),
            DmUserId = dmSable.Id,
            CreatedByUserId = dmSable.Id,
            Signups =
            [
                new SessionSignup { Character = chars[1] },
                new SessionSignup { Character = chars[3] }, // level 5 — in range, table fine
            ],
        };

        // A completed session in the past explains existing credit counters.
        var past = new GameSession
        {
            Adventure = goblinWatch,
            ScheduledAt = now.AddDays(-9),
            DmUserId = dmGareth.Id,
            CreatedByUserId = dmGareth.Id,
            Status = SessionStatus.Completed,
            CompletedAt = now.AddDays(-9).AddHours(4),
            Signups =
            [
                // Bramblefoot has already collected; Grimjaw and Elsariel still have
                // rewards waiting — visit the session page to demo the collect flow.
                new SessionSignup { Character = chars[0], ReceivedCredit = true, RewardsClaimedAt = now.AddDays(-8) },
                new SessionSignup { Character = chars[3], ReceivedCredit = true },
                new SessionSignup { Character = chars[4], ReceivedCredit = true },
            ],
        };

        db.Sessions.AddRange(s1, s2, s3, past);

        db.SessionCredits.AddRange(
            new SessionCredit { Character = chars[0], SessionId = past.Id, LevelAtAward = 3 },
            new SessionCredit { Character = chars[3], SessionId = past.Id, LevelAtAward = 5 },
            new SessionCredit { Character = chars[4], SessionId = past.Id, LevelAtAward = 11 });

        db.SessionMessages.AddRange(
            new SessionMessage { Session = s1, AuthorUserId = dmGareth.Id, Body = "Bring a climber's kit if you have one — the tower is four storeys and the stairs are 'optional'.", PostedAt = now.AddHours(-30) },
            new SessionMessage { Session = s1, AuthorUserId = pOwen.Id, Body = "Bramblefoot has rope, a grappling hook, and unearned confidence.", PostedAt = now.AddHours(-28) },
            new SessionMessage { Session = s2, AuthorUserId = pElsa.Id, Body = "I've wanted to get back to the amber hall for weeks. Who's brave enough to join, and which of you can heal?", PostedAt = now.AddHours(-10) });

        db.Announcements.AddRange(
            new Announcement
            {
                Title = "Season 3 of the West Marches begins!",
                AuthorUserId = admin.Id,
                Body = "The maps are redrawn, the frontier is open, and **the Elderwood is stirring**.\n\nNew this season:\n\n- Characters now start at **level 3** — bring a fresh sheet or promote a retainer.\n- The *Amber Court* epic runs through the summer. Watch for tagged adventures.\n- Sessions at The Dragon's Hoard every Tuesday and Thursday evening.\n\nSee you on the frontier.",
                ActiveFrom = now.AddDays(-10),
                SortOrder = 1,
            },
            new Announcement
            {
                Title = "New DMs wanted — the frontier is bigger than we are",
                AuthorUserId = admin.Id,
                Body = "Three sessions went up this week and one still needs a DM. If you've been curious about running a table, the Cartographers will pair you with a veteran for your first outing. Ask **Mira** at the store or post on any session board.",
                ActiveFrom = now.AddDays(-3),
                ActiveUntil = now.AddDays(30),
                SortOrder = 2,
            });

        // --- Demo economy: gold, an owned item, a claimed reward, and a live listing ---

        // Bramblefoot collected the past session's rewards: 150 gp + the Cloak of Billowing.
        chars[0].GoldGp = 150;
        db.LedgerEntries.Add(new LedgerEntry
        {
            CharacterId = chars[0].Id,
            Type = LedgerEntryType.RewardGold,
            GoldDelta = 150,
            SessionId = past.Id,
            Description = "Reward: The Goblin Watchtower — 150 gp.",
            OccurredAt = now.AddDays(-8),
        });

        if (cloak is not null)
        {
            var cloakInstance = new ItemInstance
            {
                CatalogItemId = cloak.Id,
                OwnerCharacterId = chars[0].Id,
                AcquiredAt = now.AddDays(-8),
            };
            db.ItemInstances.Add(cloakInstance);
            db.LedgerEntries.Add(new LedgerEntry
            {
                CharacterId = chars[0].Id,
                Type = LedgerEntryType.RewardItem,
                ItemInstanceId = cloakInstance.Id,
                ItemName = cloak.Name,
                SessionId = past.Id,
                Description = "Reward: The Goblin Watchtower — chose Cloak of Billowing (Spoils of the tower).",
                OccurredAt = now.AddDays(-8),
            });
        }

        // Elsariel has an old wand up for sale — a live marketplace listing to browse.
        if (wandOfSecrets is not null)
        {
            var wandInstance = new ItemInstance
            {
                CatalogItemId = wandOfSecrets.Id,
                OwnerCharacterId = chars[4].Id,
                Status = InstanceStatus.Listed,
                AcquiredAt = now.AddDays(-40),
            };
            db.ItemInstances.Add(wandInstance);
            db.LedgerEntries.Add(new LedgerEntry
            {
                CharacterId = chars[4].Id,
                Type = LedgerEntryType.RewardItem,
                ItemInstanceId = wandInstance.Id,
                ItemName = wandOfSecrets.Name,
                Description = "Reward: an earlier expedition — Wand of Secrets.",
                OccurredAt = now.AddDays(-40),
            });
            db.MarketListings.Add(new MarketListing
            {
                ItemInstanceId = wandInstance.Id,
                SellerCharacterId = chars[4].Id,
                AskingPriceGp = 400,
                ListedAt = now.AddDays(-2),
            });

            // A buyer needs walking-around money.
            chars[3].GoldGp = 500;
            db.LedgerEntries.Add(new LedgerEntry
            {
                CharacterId = chars[3].Id,
                Type = LedgerEntryType.RewardGold,
                GoldDelta = 500,
                Description = "Reward: earlier expeditions — accumulated bounties.",
                OccurredAt = now.AddDays(-20),
            });
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Development data seeded.");
    }

    /// <summary>
    /// Seeds the item catalog from the repo's /data reference files when the catalog is
    /// empty. Uses the same parser as the CA import screen, recorded as a normal batch.
    /// Also demonstrates the CA "campaign price" override on a couple of Varies-priced items.
    /// </summary>
    private static async Task SeedCatalogIfEmptyAsync(
        AppDbContext db,
        ICatalogFileParser parser,
        IConfiguration config,
        IHostEnvironment env,
        ILogger logger)
    {
        if (await db.CatalogItems.AnyAsync())
        {
            return;
        }

        var dataDir = config["Catalog:SeedDirectory"]
            ?? Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "data"));

        var files = new[]
        {
            (Path.Combine(dataDir, "magic-items.json"), ItemKind.Magic),
            (Path.Combine(dataDir, "equipment.json"), ItemKind.Mundane),
        };

        foreach (var (path, kind) in files)
        {
            if (!File.Exists(path))
            {
                logger.LogWarning("Catalog seed file not found: {Path} — skipping.", path);
                continue;
            }

            await using var stream = File.OpenRead(path);
            var parsed = await parser.ParseAsync(stream, kind);

            var batch = new ImportBatch
            {
                Kind = kind,
                FileName = Path.GetFileName(path),
                SourceNote = parsed.SourceNote,
                UploadedByUserId = "system-seed",
                AddedCount = parsed.Items.Count,
            };
            db.ImportBatches.Add(batch);

            foreach (var item in parsed.Items)
            {
                db.CatalogItems.Add(new CatalogItem
                {
                    Kind = kind,
                    Name = item.Name,
                    Rarity = item.Rarity,
                    Category = item.Category,
                    RequiresAttunement = item.RequiresAttunement,
                    BasePriceGp = item.BasePriceGp,
                    PriceRaw = item.PriceRaw,
                    PriceIsBasePlus = item.PriceIsBasePlus,
                    ExternalUrl = item.ExternalUrl,
                    DetailsJson = item.DetailsJson,
                    Source = CatalogSource.Imported,
                    ImportKey = item.ImportKey,
                    LastImportBatchId = batch.Id,
                });
            }

            logger.LogInformation("Seeded {Count} {Kind} catalog items from {File}.", parsed.Items.Count, kind, batch.FileName);
        }

        await db.SaveChangesAsync();

        // Demo the CA price override on two "Varies"-priced items.
        var healing = await db.CatalogItems.FirstOrDefaultAsync(c =>
            c.Name == "Potion of Healing" && c.Rarity == ItemRarity.Common);
        if (healing is not null)
        {
            healing.CampaignPriceGp = 50;
        }

        var beltRare = await db.CatalogItems.FirstOrDefaultAsync(c =>
            c.Name == "Belt of Giant Strength" && c.Rarity == ItemRarity.Rare);
        if (beltRare is not null)
        {
            beltRare.CampaignPriceGp = 6000;
        }

        await db.SaveChangesAsync();
    }
}
