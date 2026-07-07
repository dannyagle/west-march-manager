using WestMarch.Application.Adventures;
using WestMarch.Application.Characters;
using WestMarch.Application.Common;
using WestMarch.Application.Sessions;
using WestMarch.Domain.Adventures;
using WestMarch.Domain.Characters;
using WestMarch.Domain.Items;
using WestMarch.Domain.Sessions;

namespace WestMarch.Application.Items;

public record ClaimableOption(
    Guid OptionId,
    string Description,
    Guid? CatalogItemId,
    ItemRarity? Rarity,
    int? PriceGp,
    string? ExternalUrl);

public record ClaimableSet(Guid SetId, string Name, IReadOnlyList<ClaimableOption> Options);

/// <summary>One credited character's outstanding (or completed) reward claim on a session.</summary>
public record ClaimState(
    Guid CharacterId,
    string CharacterName,
    bool AlreadyClaimed,
    int GuaranteedGold,
    IReadOnlyList<string> GuaranteedOther,
    IReadOnlyList<ClaimableSet> ChoiceSets);

public interface IRewardClaimService
{
    /// <summary>Claim states for the current user's credited characters on a completed session.</summary>
    Task<IReadOnlyList<ClaimState>> GetClaimStatesAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Collects a character's rewards: guaranteed gold hits the balance, each choose-1-of-N
    /// pick mints an inventory instance (catalog-backed) or a ledger note (free text). Once only.
    /// </summary>
    Task ClaimAsync(Guid sessionId, Guid characterId, IReadOnlyDictionary<Guid, Guid> chosenOptionBySet, CancellationToken ct = default);
}

public class RewardClaimService(
    ISessionRepository sessions,
    IAdventureRepository adventures,
    ICharacterRepository characters,
    IInventoryRepository inventory,
    IUnitOfWork uow,
    ICurrentUser currentUser) : IRewardClaimService
{
    public async Task<IReadOnlyList<ClaimState>> GetClaimStatesAsync(Guid sessionId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();

        var session = await sessions.GetReadOnlyAsync(sessionId, ct)
            ?? throw new NotFoundException("Session", sessionId);

        if (session.Status != SessionStatus.Completed)
        {
            return [];
        }

        var adventure = session.Adventure;

        var goldTotal = adventure.GuaranteedRewards
            .Where(r => r.Kind == RewardKind.Gold && r.GoldAmount is not null)
            .Sum(r => r.GoldAmount!.Value);

        var otherGuaranteed = adventure.GuaranteedRewards
            .Where(r => r.Kind != RewardKind.Gold || r.GoldAmount is null)
            .Select(r => r.Description)
            .ToList();

        var sets = adventure.RewardOptionSets.Select(set => new ClaimableSet(
            set.Id,
            set.Name,
            [.. set.Options.Select(o => new ClaimableOption(
                o.Id,
                o.Description,
                o.CatalogItemId,
                o.CatalogItem?.Rarity,
                o.CatalogItem?.EffectivePriceGp,
                o.ExternalUrl ?? o.CatalogItem?.ExternalUrl))])).ToList();

        return [.. session.Signups
            .Where(su => su.ReceivedCredit == true && su.Character.OwnerUserId == userId)
            .Select(su => new ClaimState(
                su.CharacterId,
                su.Character.Name,
                su.RewardsClaimedAt is not null,
                goldTotal,
                otherGuaranteed,
                sets))];
    }

    public async Task ClaimAsync(Guid sessionId, Guid characterId, IReadOnlyDictionary<Guid, Guid> chosenOptionBySet, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();

        var session = await sessions.GetAsync(sessionId, ct)
            ?? throw new NotFoundException("Session", sessionId);

        if (session.Status != SessionStatus.Completed)
        {
            throw new AppValidationException("Rewards can only be collected after the DM completes the session.");
        }

        var signup = session.Signups.FirstOrDefault(su => su.CharacterId == characterId)
            ?? throw new NotFoundException(nameof(SessionSignup), characterId);

        if (signup.Character.OwnerUserId != userId && !currentUser.IsCa)
        {
            throw new ForbiddenAccessException("You can only collect rewards for your own characters.");
        }

        if (signup.ReceivedCredit != true)
        {
            throw new AppValidationException($"{signup.Character.Name} did not earn credit for this session.");
        }

        if (signup.RewardsClaimedAt is not null)
        {
            throw new AppValidationException($"{signup.Character.Name} already collected these rewards.");
        }

        // The session's tracked graph doesn't include the reward structure — load it with options + catalog items.
        var adventure = await adventures.GetReadOnlyAsync(session.AdventureId, ct)
            ?? throw new NotFoundException(nameof(Adventure), session.AdventureId);

        // Every choose-1-of-N set requires exactly one valid pick.
        foreach (var set in adventure.RewardOptionSets)
        {
            if (!chosenOptionBySet.TryGetValue(set.Id, out var optionId)
                || set.Options.All(o => o.Id != optionId))
            {
                throw new AppValidationException(string.IsNullOrWhiteSpace(set.Name)
                    ? "Choose one option from each reward choice."
                    : $"Choose an option from \"{set.Name}\".");
            }
        }

        var character = await characters.GetAsync(characterId, ct)
            ?? throw new NotFoundException(nameof(Character), characterId);

        // Guaranteed gold.
        var goldTotal = adventure.GuaranteedRewards
            .Where(r => r.Kind == RewardKind.Gold && r.GoldAmount is not null)
            .Sum(r => r.GoldAmount!.Value);

        if (goldTotal > 0)
        {
            character.GoldGp += goldTotal;
            inventory.AddLedger(new LedgerEntry
            {
                CharacterId = character.Id,
                Type = LedgerEntryType.RewardGold,
                GoldDelta = goldTotal,
                SessionId = session.Id,
                Description = $"Reward: {adventure.Title} — {goldTotal} gp.",
            });
        }

        // Guaranteed non-gold components are free text in the authoring editor; note them for the sheet.
        foreach (var component in adventure.GuaranteedRewards.Where(r => r.Kind != RewardKind.Gold || r.GoldAmount is null))
        {
            inventory.AddLedger(new LedgerEntry
            {
                CharacterId = character.Id,
                Type = LedgerEntryType.RewardNote,
                SessionId = session.Id,
                Description = $"Reward: {adventure.Title} — {component.Description}",
            });
        }

        // Chosen options: catalog-backed picks mint real instances; free text becomes a note.
        foreach (var set in adventure.RewardOptionSets)
        {
            var option = set.Options.First(o => o.Id == chosenOptionBySet[set.Id]);

            var setSuffix = string.IsNullOrWhiteSpace(set.Name) ? "" : $" ({set.Name})";

            if (option.CatalogItem is not null)
            {
                var instance = new ItemInstance
                {
                    CatalogItemId = option.CatalogItem.Id,
                    OwnerCharacterId = character.Id,
                };
                inventory.AddInstance(instance);

                inventory.AddLedger(new LedgerEntry
                {
                    CharacterId = character.Id,
                    Type = LedgerEntryType.RewardItem,
                    ItemInstanceId = instance.Id,
                    ItemName = option.CatalogItem.Name,
                    SessionId = session.Id,
                    Description = $"Reward: {adventure.Title} — chose {option.CatalogItem.Name}{setSuffix}.",
                });
            }
            else
            {
                inventory.AddLedger(new LedgerEntry
                {
                    CharacterId = character.Id,
                    Type = LedgerEntryType.RewardNote,
                    SessionId = session.Id,
                    Description = $"Reward: {adventure.Title} — chose {option.Description}{setSuffix}.",
                });
            }
        }

        signup.RewardsClaimedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
    }
}
