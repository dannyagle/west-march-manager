using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WestMarch.Application.Bestiary;
using WestMarch.Application.Items;
using WestMarch.Domain.Adventures;
using WestMarch.Domain.Announcements;
using WestMarch.Domain.Bestiary;
using WestMarch.Domain.Characters;
using WestMarch.Domain.Items;
using WestMarch.Domain.Sessions;
using WestMarch.Domain.Users;
using WestMarch.Infrastructure.Identity;
using WestMarch.Infrastructure.Persistence;

namespace WestMarch.Infrastructure.Seeding;

/// <summary>
/// Seeds a believable demo campaign so every screen has something to show. Runs in
/// Development automatically, and in other environments when SeedDemoData=true (for the
/// hosted test/demo site). Every step is guarded by an empty-check, so it is safe to run
/// repeatedly. All local accounts use the password "Passw0rd!".
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

        // Reference data seeds whenever it is empty — even into an existing database —
        // so an older DB picks up new files without a wipe.
        var parser = scope.ServiceProvider.GetRequiredService<ICatalogFileParser>();
        var monsterParser = scope.ServiceProvider.GetRequiredService<IMonsterFileParser>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        await SeedCatalogIfEmptyAsync(db, parser, config, env, logger);
        await SeedBestiaryIfEmptyAsync(db, monsterParser, config, env, logger);

        if (await db.Users.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding demo campaign data…");

        // ---------------------------------------------------------------- users
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
        var pPetra = await AddUser("player5@westmarch.local", "Petra Stormvale");
        var pCorvus = await AddUser("player6@westmarch.local", "Corvus Vane");
        var pWilla = await AddUser("player7@westmarch.local", "Willa Fenn");
        var pDoran = await AddUser("player8@westmarch.local", "Doran Ashford");

        // ---------------------------------------------------------------- characters
        long ddbId = 10_000_000;
        Character C(ApplicationUser owner, string name, string summary, int level, int credits = 0)
        {
            var c = new Character
            {
                OwnerUserId = owner.Id,
                Name = name,
                Summary = summary,
                Level = level,
                CreditsAtCurrentLevel = credits,
                DdbUrl = $"https://www.dndbeyond.com/characters/{++ddbId}",
                DdbCharacterId = ddbId,
            };
            db.Characters.Add(c);
            return c;
        }

        // Low/mid tier — the bread and butter of the table.
        // Levels and credit counters are kept consistent with the completed-session
        // credits seeded below (including which night each character levelled up).
        var bramblefoot = C(pOwen, "Bramblefoot", "Halfling Rogue", 4, 1);
        var percival = C(pOwen, "Sir Percival", "Human Paladin", 5, 1);
        var kirael = C(pKira, "Kirael", "Elf Druid", 4, 1);
        var grimjaw = C(pTobin, "Grimjaw", "Half-orc Barbarian", 6, 1);
        var petraIronsong = C(pPetra, "Petra Ironsong", "Dwarf Cleric", 5, 2);
        var sparrow = C(pPetra, "Sparrow", "Human Ranger", 4, 1);
        var corvus = C(pCorvus, "Corvus", "Tiefling Warlock", 7, 1);
        var willaGreen = C(pWilla, "Willa the Green", "Gnome Druid", 4, 1);
        var bramble = C(pWilla, "Bramble", "Firbolg Barbarian", 4, 0);
        var ash = C(pDoran, "Ash", "Half-elf Bard", 6, 2);
        var thornwick = C(admin, "Thornwick", "Gnome Artificer", 4, 1);
        // High tier — for the epic
        var elsariel = C(pElsa, "Elsariel Moonshadow", "Elf Wizard", 11, 1);
        var doran = C(pDoran, "Doran", "Human Fighter", 9, 2);
        var nyx = C(pCorvus, "Nyx", "Half-elf Rogue", 9, 0);

        // ---------------------------------------------------------------- reference lookups
        var monsterByName = await db.Monsters.ToDictionaryAsync(m => m.Name);
        Monster? M(string name) => monsterByName.GetValueOrDefault(name);

        var magicItems = await db.CatalogItems.Where(c => c.Kind == ItemKind.Magic).ToListAsync();
        CatalogItem? I(string name) => magicItems.FirstOrDefault(c => c.Name == name);

        static List<EncounterMonster> Muster(params (Monster? Monster, int Count)[] picks) =>
            [.. picks.Where(p => p.Monster is not null)
                .Select((p, i) => new EncounterMonster { MonsterId = p.Monster!.Id, Count = p.Count, SortOrder = i })];

        var tags = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase);
        Tag T(string name) => tags.TryGetValue(name, out var t) ? t : tags[name] = new Tag { Name = name };

        RewardOption Item(string name, int sort) => new() { Description = name, CatalogItemId = I(name)?.Id, SortOrder = sort };
        RewardOption Text(string desc, int sort, string? url = null) => new() { Description = desc, ExternalUrl = url, SortOrder = sort };

        var now = DateTimeOffset.Now;

        // ---------------------------------------------------------------- adventures
        var goblinWatch = new Adventure
        {
            Title = "The Goblin Watchtower",
            AuthorUserId = dmGareth.Id,
            MinLevel = 3,
            MaxLevel = 5,
            ShortDescription = "Smoke rises from the old border watchtower. The goblins of the Bent Fang have claimed it — and something is organizing them.",
            LongDescription = "The watchtower on the Elderline has stood empty since the retreat. Now travellers report drums at dusk and green fires on its crown. The Wardens will pay for its recapture, and pay better for whoever — or whatever — taught the Bent Fang discipline.",
            DmNotes = "The 'organizer' is a hobgoblin exile, Vex. She will parley if cornered. The tower basement hides a Warden supply cache (map to The Sunken Vault).",
            Status = AdventureStatus.Approved,
            ApprovedByUserId = admin.Id,
            ApprovedAt = now.AddDays(-40),
            ActiveFrom = now.AddDays(-50),
            Tags = [T("wilderness"), T("classic")],
            Encounters =
            [
                new Encounter
                {
                    Title = "The palisade gate", SortOrder = 0,
                    ReadAloud = "Sharpened stakes ring the tower's base, lashed with fresh goblin knots. Green fires gutter on the crown high above, and a drum starts up the moment your boots touch the causeway.",
                    Description = "Two sentries whistle for the pack below. The goblins fight in pairs and retreat up the stairs at half strength.",
                    Monsters = Muster((M("Goblin"), 8)),
                },
                new Encounter
                {
                    Title = "The tower top — Vex", SortOrder = 1,
                    Description = "Vex parleys if cornered (DC 14 Charisma). If the party negotiated, she surrenders the map satchel; if not, she fights beside her bodyguard.",
                    Npcs = [new EncounterNpc { Name = "Vex, the exile", Stats = "Use Hobgoblin, but AC 17, HP 39; parley DC 14", Description = "A disciplined hobgoblin exile teaching the Bent Fang formation fighting. Wants a warband, not a massacre.", SortOrder = 0 }],
                    Monsters = Muster((M("Hobgoblin"), 1), (M("Goblin"), 2)),
                },
            ],
            GuaranteedRewards = [new RewardComponent { Kind = RewardKind.Gold, GoldAmount = 150, Description = "150 gp Warden bounty", SortOrder = 0 }],
            RewardOptionSets =
            [
                new RewardOptionSet { Name = "Spoils of the tower", SortOrder = 0, Options =
                    [Item("Cloak of Billowing", 0), Item("Boots of Elvenkind", 1), Text("Vex's map satchel (three unexplored hex leads)", 2)] },
            ],
        };

        var hollowMill = new Adventure
        {
            Title = "The Hollow Mill",
            AuthorUserId = dmSable.Id,
            MinLevel = 3,
            MaxLevel = 4,
            ShortDescription = "The old Hollow Mill hasn't turned in years, yet last night it ground all night long. The miller's family wants to know what's grinding — and what it's grinding.",
            LongDescription = "A one-night starter on the edge of town. Rats have taken the cellars, but the rats are the least of it: a wererat clan has moved into the mill race and is 'taxing' the granary. Good for fresh level-3 blades.",
            DmNotes = "The wererat 'foreman', Skitch, will offer a cut of the grain money to be left alone. Silvered weapons matter here.",
            Status = AdventureStatus.Approved,
            ApprovedByUserId = admin.Id,
            ApprovedAt = now.AddDays(-35),
            ActiveFrom = now.AddDays(-40),
            Tags = [T("town"), T("classic")],
            Encounters =
            [
                new Encounter
                {
                    Title = "The cellar", SortOrder = 0,
                    ReadAloud = "The stairs down are slick with grain-dust and worse. Something skitters away from your light in a wave of wet little feet.",
                    Monsters = Muster((M("Swarm of Rats"), 2), (M("Giant Rat"), 4)),
                },
                new Encounter
                {
                    Title = "The mill race — Skitch's crew", SortOrder = 1,
                    Description = "Skitch parleys first, fights second. In dim light the wererats flank from the water.",
                    Npcs = [new EncounterNpc { Name = "Skitch, the foreman", Stats = "Wererat, AC 12, HP 33; only silvered/magic weapons wound him fully", Description = "A greasy, grinning wererat who thinks of himself as a small businessman.", SortOrder = 0 }],
                    Monsters = Muster((M("Wererat"), 2)),
                },
            ],
            GuaranteedRewards = [new RewardComponent { Kind = RewardKind.Gold, GoldAmount = 80, Description = "80 gp from the grateful miller", SortOrder = 0 }],
            RewardOptionSets =
            [
                new RewardOptionSet { Name = "From the mill's hidden strongbox", SortOrder = 0, Options =
                    [Item("Rope of Climbing", 0), Item("Driftglobe", 1)] },
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
            Status = AdventureStatus.Approved,
            ApprovedByUserId = admin.Id,
            ApprovedAt = now.AddDays(-30),
            ActiveFrom = now.AddDays(-32),
            Tags = [T("dungeon"), T("horror")],
            Encounters =
            [
                new Encounter
                {
                    Title = "The flooded antechamber", SortOrder = 0,
                    ReadAloud = "The vault doors hang open on drowned hinges. Inside, the waterline glimmers waist-high, and something pale moves beneath it without a ripple.",
                    Monsters = Muster((M("Ghoul"), 4)),
                },
                new Encounter
                {
                    Title = "The archive", SortOrder = 1,
                    Description = "The Archivist trades answers for fire — one truthful answer per open flame brought within reach. It attacks only if the ledgers are touched without payment.",
                    Npcs = [new EncounterNpc { Name = "The Archivist", Stats = "Flameskull — AC 13, HP 40; rejuvenates unless doused in holy water", Description = "The vault's burning-skull custodian. Pedantic, lonely, literal.", SortOrder = 0 }],
                    Monsters = Muster((M("Ghast"), 1), (M("Skeleton"), 3)),
                },
            ],
            GuaranteedRewards =
            [
                new RewardComponent { Kind = RewardKind.Gold, GoldAmount = 250, Description = "250 gp in vault coinage", SortOrder = 0 },
                new RewardComponent { Kind = RewardKind.Other, Description = "Guild favor: one free identify per character", SortOrder = 1 },
            ],
            RewardOptionSets =
            [
                new RewardOptionSet { Name = "Choose one relic", SortOrder = 0, Options =
                    [Item("Mariner's Armor", 0), Item("Cap of Water Breathing", 1), Item("Wand of Secrets", 2)] },
            ],
        };

        var saltDocks = new Adventure
        {
            Title = "The Salt Docks Murders",
            AuthorUserId = dmSable.Id,
            MinLevel = 4,
            MaxLevel = 6,
            ShortDescription = "Three dockhands dead in a week, each found bloodless at a different wharf. The harbourmaster is offering coin, and asking no questions about methods.",
            LongDescription = "A non-linear investigation across the Salt Docks. The party can pursue the leads in any order — the smuggler, the shrine, the night market — and the culprit moves depending on what they stir up. There is no 'room 1'; there is only the tide.",
            DmNotes = "The killer is a shadow bound to a cult reliquary in the sunken shrine. Cutting the cult's supply (the smuggler) or destroying the reliquary (the shrine) both end it; the night market is a red herring that reveals the shrine's location.",
            Status = AdventureStatus.Approved,
            ApprovedByUserId = admin.Id,
            ApprovedAt = now.AddDays(-18),
            ActiveFrom = now.AddDays(-20),
            Tags = [T("mystery"), T("horror"), T("town")],
            Encounters =
            [
                new Encounter
                {
                    Title = "Lead: The smuggler's warehouse", SortOrder = 0,
                    Description = "Cultist dockhands guard crates of reliquary salt. Taken alive, one names the shrine.",
                    Monsters = Muster((M("Cultist"), 4), (M("Cult Fanatic"), 1)),
                },
                new Encounter
                {
                    Title = "Lead: The night market", SortOrder = 1,
                    ReadAloud = "Lantern-light and fish-stink. A fortune-teller catches your eye and will not let go: 'You're looking for the one who drinks the dark. She's under the water, love. She's always been under the water.'",
                    Description = "A red herring that points to the shrine. No combat unless the party threatens the crowd; the harpy 'fortune-teller' flees if cornered.",
                    Npcs = [new EncounterNpc { Name = "Madame Cray", Stats = "Harpy in disguise, AC 11, HP 38", Description = "Knows more than she should, sells it a syllable at a time.", SortOrder = 0 }],
                    Monsters = Muster((M("Harpy"), 1)),
                },
                new Encounter
                {
                    Title = "The sunken shrine", SortOrder = 2,
                    ReadAloud = "Low tide bares a stair no map records, leading down into a shrine the sea has been trying to swallow. At the bottom, a reliquary pulses like a slow, cold heart.",
                    Description = "The shadow fights near the reliquary, where the dark is deep. Destroying the reliquary (AC 15, 30 hp) ends the haunting.",
                    Monsters = Muster((M("Shadow"), 3), (M("Specter"), 1)),
                },
            ],
            GuaranteedRewards = [new RewardComponent { Kind = RewardKind.Gold, GoldAmount = 300, Description = "300 gp harbourmaster's purse", SortOrder = 0 }],
            RewardOptionSets =
            [
                new RewardOptionSet { Name = "From the reliquary's hoard", SortOrder = 0, Options =
                    [Item("Ring of Protection", 0), Item("Goggles of Night", 1), Item("Sending Stones", 2)] },
            ],
        };

        var barrow = new Adventure
        {
            Title = "Barrow of the Pale Knight",
            AuthorUserId = dmGareth.Id,
            MinLevel = 5,
            MaxLevel = 7,
            ShortDescription = "The frost never leaves the Pale Knight's barrow, and this winter it has begun to spread downhill toward the village.",
            LongDescription = "A classic barrow-crawl with teeth. The Pale Knight was buried with honours and a curse; something has woken him, and his honour guard rises with him. Bring fire, and something to turn undead.",
            DmNotes = "The Wight (Pale Knight) commands the barrow's dead. Destroying his sword-hand banner breaks his hold over the guard.",
            Status = AdventureStatus.Approved,
            ApprovedByUserId = admin.Id,
            ApprovedAt = now.AddDays(-14),
            ActiveFrom = now.AddDays(-16),
            Tags = [T("dungeon"), T("horror"), T("classic")],
            Encounters =
            [
                new Encounter
                {
                    Title = "The frozen forecourt", SortOrder = 0,
                    ReadAloud = "Rime sheathes every stone. The honour guard stands in ranks where they were buried, and as one they turn their frost-blind faces toward you.",
                    Monsters = Muster((M("Skeleton"), 6), (M("Zombie"), 2)),
                },
                new Encounter
                {
                    Title = "The inner barrow — the Pale Knight", SortOrder = 1,
                    Description = "The Wight fights from the dais. Two gargoyle 'grave-wardens' unfold from the corners on round 2.",
                    Npcs = [new EncounterNpc { Name = "The Pale Knight", Stats = "Wight, AC 14, HP 45; life drain; commands nearby undead", Description = "A dead lord who does not know, or will not accept, that the war is over.", SortOrder = 0 }],
                    Monsters = Muster((M("Wight"), 1), (M("Gargoyle"), 2)),
                },
            ],
            GuaranteedRewards = [new RewardComponent { Kind = RewardKind.Gold, GoldAmount = 400, Description = "400 gp in barrow-gold and grave-goods", SortOrder = 0 }],
            RewardOptionSets =
            [
                new RewardOptionSet { Name = "The Pale Knight's regalia", SortOrder = 0, Options =
                    [Item("Flame Tongue", 0), Item("Bracers of Defense", 1), Item("Amulet of Health", 2)] },
            ],
        };

        var wyrmlight = new Adventure
        {
            Title = "Wyrmlight Crossing",
            AuthorUserId = admin.Id,
            MinLevel = 7,
            MaxLevel = 9,
            ShortDescription = "The bridge at Wyrmlight Crossing has been claimed by something that demands a toll in gold — or in travellers. The trade road is closing, one caravan at a time.",
            LongDescription = "A heavier expedition for seasoned parties. The 'toll-keeper' is a manticore that has learned it can extort a whole road; a manticore is a problem, but the bandit company that has taken to working alongside it is the real war.",
            DmNotes = "The manticore is smart and cowardly — it negotiates, then betrays. The Bandit Captain, Roan, will change sides for the right price.",
            Status = AdventureStatus.Approved,
            ApprovedByUserId = admin.Id,
            ApprovedAt = now.AddDays(-10),
            ActiveFrom = now.AddDays(-12),
            Tags = [T("wilderness"), T("epic event")],
            Encounters =
            [
                new Encounter
                {
                    Title = "The tollgate ambush", SortOrder = 0,
                    ReadAloud = "A chain across the bridge, and a dozen crossbows in the rocks above. A voice: 'The toll's gone up. It's everything you have, and one of you stays.'",
                    Description = "Roan's company opens the parley. The dire wolves are loosed if steel is drawn.",
                    Npcs = [new EncounterNpc { Name = "Roan, Bandit Captain", Stats = "Bandit Captain, AC 15, HP 65", Description = "Runs the crossing like a business. Loyal to coin, not to the beast on the bridge.", SortOrder = 0 }],
                    Monsters = Muster((M("Bandit"), 6), (M("Bandit Captain"), 1), (M("Dire Wolf"), 2)),
                },
                new Encounter
                {
                    Title = "Under the span — the toll-keeper", SortOrder = 1,
                    Description = "The manticore fights from the air, raking tail-spikes, and flees below half HP to renegotiate.",
                    Monsters = Muster((M("Manticore"), 1)),
                },
            ],
            GuaranteedRewards = [new RewardComponent { Kind = RewardKind.Gold, GoldAmount = 650, Description = "650 gp: reopened-road bounty + reclaimed tolls", SortOrder = 0 }],
            RewardOptionSets =
            [
                new RewardOptionSet { Name = "The toll-keeper's hoard", SortOrder = 0, Options =
                    [Item("Sun Blade", 0), Item("Javelin of Lightning", 1), Item("Gauntlets of Ogre Power", 2)] },
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
            Status = AdventureStatus.Approved,
            ApprovedByUserId = admin.Id,
            ApprovedAt = now.AddDays(-7),
            ActiveFrom = now.AddDays(-7),
            ActiveUntil = now.AddDays(60),
            Tags = [T("epic event"), T("mystery"), T("wilderness")],
            Encounters =
            [
                new Encounter
                {
                    Title = "The overgrown approach", SortOrder = 0,
                    Description = "The grove defends itself. The treant stands down if the party proves they mean the hall no harm (extinguish all open flames).",
                    Monsters = Muster((M("Treant"), 1), (M("Awakened Tree"), 4)),
                },
                new Encounter
                {
                    Title = "The amber hall", SortOrder = 1,
                    ReadAloud = "Amber swallows the hall like honey poured over a feast — tables, banners, and courtiers mid-toast. At the far end, a crowned figure sits unswallowed, and his eyes follow you.",
                    Description = "The King wakes if the amber is damaged. He speaks only in questions. Use an Archmage chassis; lair action: amber grasp (DC 16 STR or restrained).",
                    Npcs = [new EncounterNpc { Name = "The King in Amber", Stats = "Archmage chassis — AC 12 (15 w/ mage armor), HP 99; lair: amber grasp DC 16 STR", Description = "A monarch of no recorded court. Answers only with questions; each honest answer given to him costs a memory.", SortOrder = 0 }],
                },
            ],
            GuaranteedRewards = [new RewardComponent { Kind = RewardKind.Gold, GoldAmount = 900, Description = "900 gp: Guild expedition purse", SortOrder = 0 }],
            RewardOptionSets =
            [
                new RewardOptionSet { Name = "Boon of the Amber Court", SortOrder = 0, Options =
                    [Item("Staff of Withering", 0), Text("Amber shard: once, ask the King one question (DM adjudicates)", 1), Text("Ring of Resistance (poison)", 2, "http://dnd2024.wikidot.com/wondrous-items:ring-of-resistance")] },
            ],
        };

        var whisperingMine = new Adventure
        {
            Title = "The Whispering Mine",
            AuthorUserId = dmSable.Id,
            MinLevel = 5,
            MaxLevel = 7,
            ShortDescription = "The silver mine was abandoned when the digging started answering back. The company wants it reopened; the miners want it sealed.",
            LongDescription = "Draft submitted for review. A descent into a mine where a gargoyle brood has nested around a vein of something that hums. Needs a second pass on the middle encounters before it's ready for the table.",
            DmNotes = "Deciding whether the 'whisper' is a real entity or mass hysteria + echoing gargoyles. Leaning real.",
            Status = AdventureStatus.ReadyForReview,
            ActiveFrom = now.AddDays(-4),
            Tags = [T("dungeon"), T("mystery")],
            Encounters =
            [
                new Encounter
                {
                    Title = "The rope-lift shaft", SortOrder = 0,
                    Description = "Gargoyles roost in the shaft and drop on descending parties.",
                    Monsters = Muster((M("Gargoyle"), 3)),
                },
            ],
            GuaranteedRewards = [new RewardComponent { Kind = RewardKind.Gold, GoldAmount = 350, Description = "350 gp reopening bonus", SortOrder = 0 }],
            RewardOptionSets =
            [
                new RewardOptionSet { Name = "From the humming vein", SortOrder = 0, Options =
                    [Item("Pearl of Power", 0), Item("Eyes of the Eagle", 1)] },
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
            DmNotes = "TODO: decide who's lying. Maybe fold this into The Hollow Mill instead.",
            Status = AdventureStatus.Draft,
            ActiveFrom = now,
            Tags = [T("mystery"), T("town")],
        };

        db.Adventures.AddRange(goblinWatch, hollowMill, sunkenVault, saltDocks, barrow, wyrmlight, kingInAmber, whisperingMine, draftIdea);

        // ---------------------------------------------------------------- sessions
        DateTimeOffset Evening(int daysOut, int hour = 18) =>
            new(DateOnly.FromDateTime(now.LocalDateTime.Date.AddDays(daysOut)), new TimeOnly(hour, 30), now.Offset);

        // A completed session: everyone signed up earned credit; a matching SessionCredit is logged.
        // "levelUps" lists characters whose credit that night triggered an advancement (for the deeds timeline).
        GameSession Past(Adventure adv, ApplicationUser dm, int daysAgo, IEnumerable<Character> party, string? location = null, IEnumerable<Character>? levelUps = null)
        {
            var when = Evening(-daysAgo);
            var levelUpSet = levelUps?.ToHashSet() ?? [];
            var session = new GameSession
            {
                Adventure = adv,
                ScheduledAt = when,
                DmUserId = dm.Id,
                CreatedByUserId = dm.Id,
                Status = SessionStatus.Completed,
                CompletedAt = when.AddHours(4),
                Location = location ?? "The Dragon's Hoard",
                Signups = [.. party.Select(c => new SessionSignup { Character = c, ReceivedCredit = true, SignedUpAt = when.AddDays(-3) })],
            };
            foreach (var c in party)
            {
                db.SessionCredits.Add(new SessionCredit
                {
                    Character = c,
                    SessionId = session.Id,
                    LevelAtAward = levelUpSet.Contains(c) ? c.Level - 1 : c.Level,
                    TriggeredLevelUp = levelUpSet.Contains(c),
                    AwardedAt = session.CompletedAt!.Value,
                });
            }
            db.Sessions.Add(session);
            return session;
        }

        GameSession Scheduled(Adventure adv, ApplicationUser? dm, ApplicationUser createdBy, int daysAhead, IEnumerable<Character> party, string? location = null)
        {
            var session = new GameSession
            {
                Adventure = adv,
                ScheduledAt = Evening(daysAhead),
                DmUserId = dm?.Id,
                CreatedByUserId = createdBy.Id,
                Location = location ?? "The Dragon's Hoard",
                Signups = [.. party.Select(c => new SessionSignup { Character = c, SignedUpAt = now.AddDays(-1) })],
            };
            db.Sessions.Add(session);
            return session;
        }

        // Seven completed expeditions, each with at least four adventurers.
        // House rule (enforced at signup by SessionService): one character per player
        // per session — every party below uses distinct players.
        // The three most recent (4, 2, and 1 days ago — one per DM) populate the
        // "Recently run" list on Behind the Screen for every demo DM login.
        var pastHollow2 = Past(hollowMill, admin, 30, [bramblefoot, kirael, sparrow, bramble, thornwick], levelUps: [bramblefoot]);
        var pastVault = Past(sunkenVault, dmGareth, 24, [percival, grimjaw, petraIronsong, ash]);
        var pastGoblin = Past(goblinWatch, dmGareth, 18, [bramblefoot, kirael, sparrow, willaGreen, thornwick], levelUps: [kirael, sparrow, thornwick, willaGreen]);
        var pastDocks = Past(saltDocks, dmSable, 12, [grimjaw, ash, petraIronsong, percival], levelUps: [percival, petraIronsong, ash]);
        var pastBarrow = Past(barrow, dmGareth, 4, [corvus, grimjaw, percival, ash, petraIronsong], levelUps: [grimjaw]);
        var pastHollow1 = Past(hollowMill, dmSable, 2, [bramble, sparrow, thornwick, kirael], levelUps: [bramble]);
        var pastDocks2 = Past(saltDocks, admin, 1, [ash, petraIronsong, willaGreen, grimjaw]);

        // Six upcoming expeditions: two still need a DM (the DM board), the rest are looking for players.
        var upKing = Scheduled(kingInAmber, null, pElsa, 6, [elsariel, doran], "The Dragon's Hoard — main hall");          // needs a DM (epic)
        var upDocks = Scheduled(saltDocks, null, pCorvus, 9, [corvus, ash], "The Salt Docks");                            // needs a DM
        var upGoblin = Scheduled(goblinWatch, dmGareth, dmGareth, 3, [bramblefoot, kirael], "The Dragon's Hoard — back table");
        var upBarrow = Scheduled(barrow, dmSable, dmSable, 7, [grimjaw, corvus, percival]);
        var upWyrm = Scheduled(wyrmlight, dmGareth, dmGareth, 11, [doran, nyx, percival]);                                // percival is off-tier — demos the roster flag
        var upHollow = Scheduled(hollowMill, admin, admin, 5, [sparrow], "The Dragon's Hoard — corner booth");            // looking for players

        // ---------------------------------------------------------------- message boards
        db.SessionMessages.AddRange(
            new SessionMessage { Session = upGoblin, AuthorUserId = dmGareth.Id, Body = "Bring a climber's kit if you have one — the tower is four storeys and the stairs are 'optional'.", PostedAt = now.AddHours(-30) },
            new SessionMessage { Session = upGoblin, AuthorUserId = pOwen.Id, Body = "Bramblefoot has rope, a grappling hook, and unearned confidence.", PostedAt = now.AddHours(-28) },
            new SessionMessage { Session = upKing, AuthorUserId = pElsa.Id, Body = "I've wanted to get back to the amber hall for weeks. We need a DM brave enough to run it — and a healer. Any takers?", PostedAt = now.AddHours(-20) },
            new SessionMessage { Session = upKing, AuthorUserId = pDoran.Id, Body = "Doran's in. Nine levels of stubborn and a very large sword.", PostedAt = now.AddHours(-18) },
            new SessionMessage { Session = upHollow, AuthorUserId = admin.Id, Body = "New-player friendly! If you've got a fresh level-3 and want a gentle first outing, this is the one. Two seats open.", PostedAt = now.AddHours(-6) });

        // ---------------------------------------------------------------- announcements
        db.Announcements.AddRange(
            new Announcement
            {
                Title = "Season 3 of the West Marches begins!",
                AuthorUserId = admin.Id,
                Body = "The maps are redrawn, the frontier is open, and **the Elderwood is stirring**.\n\nNew this season:\n\n- Characters now start at **level 3** — bring a fresh sheet or promote a retainer.\n- The *Amber Court* epic runs through the summer. Watch for tagged adventures.\n- Sessions at The Dragon's Hoard every Tuesday and Thursday evening.\n\nSee you on the frontier.",
                ActiveFrom = now.AddDays(-10), SortOrder = 1,
            },
            new Announcement
            {
                Title = "New DMs wanted — the frontier is bigger than we are",
                AuthorUserId = admin.Id,
                Body = "Two expeditions are up and still need a Dungeon Master, including the *King in Amber* epic. If you've been curious about running a table, the Cartographers will pair you with a veteran for your first outing. Ask **Mira** at the store or post on any session board.",
                ActiveFrom = now.AddDays(-3), ActiveUntil = now.AddDays(30), SortOrder = 2,
            },
            new Announcement
            {
                Title = "Marketplace open: sell what you've outgrown",
                AuthorUserId = admin.Id,
                Body = "The Bazaar of the March is live. Retired an item? List it for other characters to buy, or quick-sell it to the caravan for half value. A few blades are already on offer — go see.",
                ActiveFrom = now.AddDays(-1), SortOrder = 3,
            });

        await db.SaveChangesAsync();

        // ---------------------------------------------------------------- economy & rewards
        // Helper: grant a claimed reward (gold + optional item) and mark the signup collected.
        void Claim(GameSession session, Character c, int gold, string? itemName, string adventureTitle, string setName, int daysAgo)
        {
            var when = now.AddDays(-daysAgo);
            var signup = session.Signups.First(s => s.CharacterId == c.Id);
            signup.RewardsClaimedAt = when;

            if (gold > 0)
            {
                c.GoldGp += gold;
                db.LedgerEntries.Add(new LedgerEntry
                {
                    CharacterId = c.Id, Type = LedgerEntryType.RewardGold, GoldDelta = gold,
                    SessionId = session.Id, Description = $"Reward: {adventureTitle} — {gold} gp.", OccurredAt = when,
                });
            }

            if (itemName is not null && I(itemName) is { } item)
            {
                var instance = new ItemInstance { CatalogItemId = item.Id, OwnerCharacterId = c.Id, AcquiredAt = when };
                db.ItemInstances.Add(instance);
                db.LedgerEntries.Add(new LedgerEntry
                {
                    CharacterId = c.Id, Type = LedgerEntryType.RewardItem, ItemInstanceId = instance.Id, ItemName = item.Name,
                    SessionId = session.Id, Description = $"Reward: {adventureTitle} — chose {item.Name} ({setName}).", OccurredAt = when,
                });
            }
        }

        // Most veterans have collected their older rewards…
        Claim(pastHollow2, bramblefoot, 80, "Rope of Climbing", "The Hollow Mill", "From the mill's hidden strongbox", 30);
        Claim(pastHollow2, kirael, 80, "Driftglobe", "The Hollow Mill", "From the mill's hidden strongbox", 30);
        Claim(pastVault, percival, 250, "Mariner's Armor", "The Sunken Vault of Aldremir", "Choose one relic", 24);
        Claim(pastVault, grimjaw, 250, null, "The Sunken Vault of Aldremir", "Choose one relic", 24);
        Claim(pastGoblin, bramblefoot, 150, "Cloak of Billowing", "The Goblin Watchtower", "Spoils of the tower", 18);
        Claim(pastGoblin, kirael, 150, "Boots of Elvenkind", "The Goblin Watchtower", "Spoils of the tower", 18);
        Claim(pastDocks, grimjaw, 300, "Ring of Protection", "The Salt Docks Murders", "From the reliquary's hoard", 12);
        Claim(pastDocks, ash, 300, "Goggles of Night", "The Salt Docks Murders", "From the reliquary's hoard", 12);
        Claim(pastDocks, percival, 300, null, "The Salt Docks Murders", "From the reliquary's hoard", 12);

        // …but the three most recent runs (Barrow 4d, Hollow Mill 2d, Salt Docks 1d) are
        // UNCOLLECTED — visit those session pages as any of their players to demo the
        // "Collect your rewards" flow. These also reset to "recent" whenever the demo
        // data is re-seeded, keeping the Behind the Screen list populated.

        // Give a couple of high-level characters some standing coin for the marketplace demo.
        elsariel.GoldGp += 1200;
        db.LedgerEntries.Add(new LedgerEntry { CharacterId = elsariel.Id, Type = LedgerEntryType.RewardGold, GoldDelta = 1200, Description = "Reward: earlier expeditions — accumulated bounties.", OccurredAt = now.AddDays(-25) });
        doran.GoldGp += 900;
        db.LedgerEntries.Add(new LedgerEntry { CharacterId = doran.Id, Type = LedgerEntryType.RewardGold, GoldDelta = 900, Description = "Reward: earlier expeditions — accumulated bounties.", OccurredAt = now.AddDays(-22) });

        // ---------------------------------------------------------------- marketplace listings
        void List(Character seller, string itemName, int price, int daysAgo)
        {
            if (I(itemName) is not { } item)
            {
                return;
            }
            var instance = new ItemInstance { CatalogItemId = item.Id, OwnerCharacterId = seller.Id, Status = InstanceStatus.Listed, AcquiredAt = now.AddDays(-daysAgo - 10) };
            db.ItemInstances.Add(instance);
            db.LedgerEntries.Add(new LedgerEntry { CharacterId = seller.Id, Type = LedgerEntryType.RewardItem, ItemInstanceId = instance.Id, ItemName = item.Name, Description = $"Reward: an earlier expedition — {item.Name}.", OccurredAt = now.AddDays(-daysAgo - 10) });
            db.MarketListings.Add(new MarketListing { ItemInstanceId = instance.Id, SellerCharacterId = seller.Id, AskingPriceGp = price, ListedAt = now.AddDays(-daysAgo) });
        }

        // A spread of rarities so the marketplace filters have something to bite on —
        // and a bargain-priced Rare that a mid-level character can afford but is
        // out-of-tier for (demoing the warn-don't-block purchase flow).
        List(bramblefoot, "Moon-Touched Sword", 60, 4);        // Common
        List(elsariel, "Wand of Secrets", 400, 3);             // Uncommon
        List(doran, "Gauntlets of Ogre Power", 500, 2);        // Uncommon
        List(corvus, "Immovable Rod", 260, 1);                 // Uncommon
        List(nyx, "Boots of Levitation", 450, 2);              // Rare, priced to move — Grimjaw (lvl 6) can afford it, off-tier
        List(elsariel, "Cloak of Displacement", 3800, 5);      // Rare
        List(doran, "Ring of Regeneration", 38000, 6);         // Very Rare — aspirational shelf stock

        await db.SaveChangesAsync();
        logger.LogInformation("Demo campaign data seeded.");
    }

    /// <summary>Resolves the folder holding the reference JSON files: config override,
    /// then the published copy next to the app, then the repo's /data folder in dev.</summary>
    private static string ResolveDataDir(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Catalog:SeedDirectory"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var published = Path.Combine(env.ContentRootPath, "data");
        return Directory.Exists(published)
            ? published
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "data"));
    }

    /// <summary>
    /// Seeds the item catalog from the reference files when empty. Uses the same parser as the
    /// CA import screen, recorded as a normal batch, and demos the CA price override on two items.
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

        var dataDir = ResolveDataDir(config, env);

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
                Kind = (ImportFileKind)(int)kind, // Mundane/Magic share numeric values by design
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
        var healing = await db.CatalogItems.FirstOrDefaultAsync(c => c.Name == "Potion of Healing" && c.Rarity == ItemRarity.Common);
        if (healing is not null)
        {
            healing.CampaignPriceGp = 50;
        }

        var beltRare = await db.CatalogItems.FirstOrDefaultAsync(c => c.Name == "Belt of Giant Strength" && c.Rarity == ItemRarity.Rare);
        if (beltRare is not null)
        {
            beltRare.CampaignPriceGp = 6000;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Seeds the bestiary from the monster reference file when empty.</summary>
    private static async Task SeedBestiaryIfEmptyAsync(
        AppDbContext db,
        IMonsterFileParser parser,
        IConfiguration config,
        IHostEnvironment env,
        ILogger logger)
    {
        if (await db.Monsters.AnyAsync())
        {
            return;
        }

        var path = Path.Combine(ResolveDataDir(config, env), "srd_5e_monsters_ext.json");

        if (!File.Exists(path))
        {
            logger.LogWarning("Bestiary seed file not found: {Path} — skipping.", path);
            return;
        }

        await using var stream = File.OpenRead(path);
        var parsed = await parser.ParseAsync(stream);

        var batch = new ImportBatch
        {
            Kind = ImportFileKind.Monster,
            FileName = Path.GetFileName(path),
            SourceNote = parsed.SourceNote,
            UploadedByUserId = "system-seed",
            AddedCount = parsed.Monsters.Count,
        };
        db.ImportBatches.Add(batch);

        foreach (var m in parsed.Monsters)
        {
            db.Monsters.Add(new Monster
            {
                Name = m.Name,
                ChallengeRating = m.ChallengeRating,
                CrValue = m.CrValue,
                Xp = m.Xp,
                ArmorClass = m.ArmorClass,
                MaxHitPoints = m.MaxHitPoints,
                HitDice = m.HitDice,
                Size = m.Size,
                CreatureType = m.CreatureType,
                Alignment = m.Alignment,
                StatsJson = m.StatsJson,
                ImportKey = m.ImportKey,
                LastImportBatchId = batch.Id,
            });
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} monsters from {File}.", parsed.Monsters.Count, batch.FileName);
    }
}
