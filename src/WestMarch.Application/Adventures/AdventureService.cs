using WestMarch.Application.Common;
using WestMarch.Domain.Adventures;
using WestMarch.Domain.Characters;

namespace WestMarch.Application.Adventures;

public record RewardComponentInput(RewardKind Kind, int? GoldAmount, string Description);

/// <summary>Catalog-backed when CatalogItemId is set (Description becomes a display snapshot); free text otherwise.</summary>
public record RewardOptionInput(string Description, string? ExternalUrl, Guid? CatalogItemId = null);

public record RewardOptionSetInput(string Name, List<RewardOptionInput> Options);

public record AdventureInput(
    string Title,
    int MinLevel,
    int MaxLevel,
    int TargetPlayersMin,
    int TargetPlayersMax,
    string ShortDescription,
    string LongDescription,
    string? DmNotes,
    string? MonsterStatBlocks,
    DateTimeOffset ActiveFrom,
    DateTimeOffset? ActiveUntil,
    List<string> Tags,
    List<RewardComponentInput> GuaranteedRewards,
    List<RewardOptionSetInput> RewardOptionSets);

public interface IAdventureService
{
    /// <summary>Adventures visible to the caller for the authoring/review screens (DM/CA).</summary>
    Task<IReadOnlyList<Adventure>> ListForAuthoringAsync(CancellationToken ct = default);

    /// <summary>Approved adventures active on the given date — the pool any player can schedule a session from.</summary>
    Task<IReadOnlyList<Adventure>> ListSchedulableAsync(DateTimeOffset onDate, CancellationToken ct = default);

    /// <summary>Loads an adventure the caller may see. DM-only fields are stripped for non-DM callers.</summary>
    Task<Adventure> GetAsync(Guid id, CancellationToken ct = default);

    Task<Adventure> CreateAsync(AdventureInput input, CancellationToken ct = default);
    Task<Adventure> UpdateAsync(Guid id, AdventureInput input, CancellationToken ct = default);

    Task SubmitForReviewAsync(Guid id, CancellationToken ct = default);
    Task ApproveAsync(Guid id, CancellationToken ct = default);
    Task ReturnToDraftAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken ct = default);
}

public class AdventureService(
    IAdventureRepository adventures,
    ITagRepository tags,
    Items.ICatalogRepository catalog,
    IUnitOfWork uow,
    ICurrentUser currentUser) : IAdventureService
{
    public async Task<IReadOnlyList<Adventure>> ListForAuthoringAsync(CancellationToken ct = default)
    {
        RequireDm();
        var all = await adventures.ListAsync(ct);
        return [.. all.Where(CanSee)];
    }

    public async Task<IReadOnlyList<Adventure>> ListSchedulableAsync(DateTimeOffset onDate, CancellationToken ct = default)
    {
        currentUser.RequireUserId();
        var list = await adventures.ListApprovedActiveAsync(onDate, ct);
        return [.. list.Select(SanitizeForCaller)];
    }

    public async Task<Adventure> GetAsync(Guid id, CancellationToken ct = default)
    {
        currentUser.RequireUserId();
        var adventure = await adventures.GetReadOnlyAsync(id, ct)
            ?? throw new NotFoundException(nameof(Adventure), id);

        if (!CanSee(adventure))
        {
            // Hide existence of invisible drafts.
            throw new NotFoundException(nameof(Adventure), id);
        }

        return SanitizeForCaller(adventure);
    }

    public async Task<Adventure> CreateAsync(AdventureInput input, CancellationToken ct = default)
    {
        RequireDm();
        Validate(input);

        var adventure = new Adventure
        {
            AuthorUserId = currentUser.RequireUserId(),
            Status = AdventureStatus.Draft,
        };

        await ApplyAsync(adventure, input, ct);
        adventures.Add(adventure);
        await uow.SaveChangesAsync(ct);
        return adventure;
    }

    public async Task<Adventure> UpdateAsync(Guid id, AdventureInput input, CancellationToken ct = default)
    {
        var adventure = await GetForMutationAsync(id, ct);

        // Authors may edit their own adventures until approval; CAs may always edit.
        var isAuthor = adventure.AuthorUserId == currentUser.RequireUserId();
        if (!currentUser.IsCa && !(isAuthor && adventure.Status != AdventureStatus.Approved))
        {
            throw new ForbiddenAccessException(adventure.Status == AdventureStatus.Approved
                ? "Approved adventures can only be edited by a Campaign Admin."
                : "Only the author or a Campaign Admin can edit this adventure.");
        }

        Validate(input);
        await ApplyAsync(adventure, input, ct);
        adventure.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
        return adventure;
    }

    public async Task SubmitForReviewAsync(Guid id, CancellationToken ct = default)
    {
        var adventure = await GetForMutationAsync(id, ct);

        if (adventure.AuthorUserId != currentUser.RequireUserId() && !currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Only the author can submit an adventure for review.");
        }

        if (adventure.Status != AdventureStatus.Draft)
        {
            throw new AppValidationException("Only drafts can be submitted for review.");
        }

        adventure.Status = AdventureStatus.ReadyForReview;
        adventure.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
    }

    public async Task ApproveAsync(Guid id, CancellationToken ct = default)
    {
        if (!currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Only a Campaign Admin can approve adventures.");
        }

        var adventure = await GetForMutationAsync(id, ct);

        if (adventure.Status != AdventureStatus.ReadyForReview)
        {
            throw new AppValidationException("Only adventures marked Ready for Review can be approved.");
        }

        adventure.Status = AdventureStatus.Approved;
        adventure.ApprovedByUserId = currentUser.UserId;
        adventure.ApprovedAt = DateTimeOffset.UtcNow;
        adventure.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
    }

    public async Task ReturnToDraftAsync(Guid id, CancellationToken ct = default)
    {
        var adventure = await GetForMutationAsync(id, ct);

        var isAuthor = adventure.AuthorUserId == currentUser.RequireUserId();
        if (!currentUser.IsCa && !isAuthor)
        {
            throw new ForbiddenAccessException("Only the author or a Campaign Admin can return an adventure to draft.");
        }

        if (adventure.Status == AdventureStatus.Approved && !currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Only a Campaign Admin can withdraw an approved adventure.");
        }

        adventure.Status = AdventureStatus.Draft;
        adventure.ApprovedByUserId = null;
        adventure.ApprovedAt = null;
        adventure.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken ct = default) => tags.ListAsync(ct);

    private async Task<Adventure> GetForMutationAsync(Guid id, CancellationToken ct)
    {
        RequireDm();
        return await adventures.GetAsync(id, ct) ?? throw new NotFoundException(nameof(Adventure), id);
    }

    /// <summary>Draft → author + CA. ReadyForReview → all DMs + CAs. Approved → everyone.</summary>
    private bool CanSee(Adventure adventure) => adventure.Status switch
    {
        AdventureStatus.Draft => currentUser.IsCa || adventure.AuthorUserId == currentUser.UserId,
        AdventureStatus.ReadyForReview => currentUser.IsDm,
        AdventureStatus.Approved => true,
        _ => false,
    };

    /// <summary>Strips DM-only fields for callers without the DM role. Reads are untracked, so this never persists.</summary>
    private Adventure SanitizeForCaller(Adventure adventure)
    {
        if (!currentUser.IsDm)
        {
            adventure.DmNotes = null;
            adventure.MonsterStatBlocks = null;
        }

        return adventure;
    }

    private async Task ApplyAsync(Adventure adventure, AdventureInput input, CancellationToken ct)
    {
        adventure.Title = input.Title.Trim();
        adventure.MinLevel = input.MinLevel;
        adventure.MaxLevel = input.MaxLevel;
        adventure.TargetPlayersMin = input.TargetPlayersMin;
        adventure.TargetPlayersMax = input.TargetPlayersMax;
        adventure.ShortDescription = input.ShortDescription.Trim();
        adventure.LongDescription = input.LongDescription.Trim();
        adventure.DmNotes = string.IsNullOrWhiteSpace(input.DmNotes) ? null : input.DmNotes.Trim();
        adventure.MonsterStatBlocks = string.IsNullOrWhiteSpace(input.MonsterStatBlocks) ? null : input.MonsterStatBlocks.Trim();
        adventure.ActiveFrom = input.ActiveFrom;
        adventure.ActiveUntil = input.ActiveUntil;

        var tagNames = input.Tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        adventure.Tags.Clear();
        adventure.Tags.AddRange(await tags.GetOrCreateAsync(tagNames, ct));

        // Rewards are replaced wholesale on save — the editor round-trips the full structure.
        // New rows are registered through the repository (not just the navigation) so the
        // change tracker sees them as Added despite their pre-generated Guid keys.
        adventures.RemoveRewardComponents(adventure.GuaranteedRewards);
        adventure.GuaranteedRewards.Clear();
        var newComponents = input.GuaranteedRewards.Select((r, i) => new RewardComponent
        {
            AdventureId = adventure.Id,
            Kind = r.Kind,
            GoldAmount = r.Kind == RewardKind.Gold ? r.GoldAmount : null,
            Description = r.Description.Trim(),
            SortOrder = i,
        }).ToList();
        // Repository-only adds: relationship fixup populates the navigation collections,
        // so also AddRange-ing them here would double them up on tracked adventures.
        adventures.AddRewardComponents(newComponents);

        // Catalog-backed options: verify the referenced items exist and snapshot their names.
        var catalogIds = input.RewardOptionSets
            .SelectMany(s => s.Options)
            .Where(o => o.CatalogItemId is not null)
            .Select(o => o.CatalogItemId!.Value)
            .Distinct()
            .ToList();
        var catalogItems = (await catalog.ListByIdsAsync(catalogIds, ct)).ToDictionary(c => c.Id);

        var missing = catalogIds.Where(id => !catalogItems.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new AppValidationException("A reward option references a catalog item that no longer exists.");
        }

        adventures.RemoveRewardOptionSets(adventure.RewardOptionSets);
        adventure.RewardOptionSets.Clear();
        var newSets = input.RewardOptionSets.Select((set, i) => new RewardOptionSet
        {
            AdventureId = adventure.Id,
            Name = set.Name.Trim(),
            SortOrder = i,
            Options = [.. set.Options.Select((o, j) => new RewardOption
            {
                Description = o.CatalogItemId is not null
                    ? catalogItems[o.CatalogItemId.Value].Name
                    : o.Description.Trim(),
                ExternalUrl = string.IsNullOrWhiteSpace(o.ExternalUrl) ? null : o.ExternalUrl.Trim(),
                CatalogItemId = o.CatalogItemId,
                SortOrder = j,
            })],
        }).ToList();
        adventures.AddRewardOptionSets(newSets);
    }

    private void RequireDm()
    {
        if (!currentUser.IsDm)
        {
            throw new ForbiddenAccessException("DM role required.");
        }
    }

    private static void Validate(AdventureInput input)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors.Add("Title is required.");
        }

        if (string.IsNullOrWhiteSpace(input.ShortDescription))
        {
            errors.Add("A public short description is required.");
        }

        if (string.IsNullOrWhiteSpace(input.LongDescription))
        {
            errors.Add("A public long description is required.");
        }

        if (input.MinLevel < LevelingEngine.MinLevel || input.MaxLevel > LevelingEngine.MaxLevel || input.MinLevel > input.MaxLevel)
        {
            errors.Add($"Level range must be within {LevelingEngine.MinLevel}–{LevelingEngine.MaxLevel} and min ≤ max.");
        }

        if (input.TargetPlayersMin < 1 || input.TargetPlayersMin > input.TargetPlayersMax)
        {
            errors.Add("Target party size must be at least 1 and min ≤ max.");
        }

        if (input.ActiveUntil is not null && input.ActiveUntil < input.ActiveFrom)
        {
            errors.Add("The active window must end after it starts.");
        }

        // Set names are optional flavor ("Boon of the Wardens"); the UI defaults to "Choose one".
        var setNumber = 0;
        foreach (var set in input.RewardOptionSets)
        {
            setNumber++;
            var label = string.IsNullOrWhiteSpace(set.Name) ? $"Reward choice {setNumber}" : $"'{set.Name}'";

            if (set.Options.Count < 2)
            {
                errors.Add($"{label} needs at least two options to be a choice.");
            }
            else if (set.Options.Any(o => string.IsNullOrWhiteSpace(o.Description) && o.CatalogItemId is null))
            {
                errors.Add($"Every option in {label} needs a description or a linked catalog item.");
            }
        }

        foreach (var reward in input.GuaranteedRewards)
        {
            if (string.IsNullOrWhiteSpace(reward.Description))
            {
                errors.Add("Every guaranteed reward needs a description.");
            }

            if (reward.Kind == RewardKind.Gold && reward.GoldAmount is null or < 0)
            {
                errors.Add("Gold rewards need a non-negative amount.");
            }
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException([.. errors]);
        }
    }
}
