using WestMarch.Application.Characters;
using WestMarch.Application.Common;
using WestMarch.Domain.Characters;
using WestMarch.Domain.Items;

namespace WestMarch.Application.Items;

public interface IInventoryService
{
    /// <summary>Owned + listed items for a character. Visible to the owner, DMs, and CAs.</summary>
    Task<IReadOnlyList<ItemInstance>> GetInventoryAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>The character's transaction history, newest first. Same visibility as the inventory.</summary>
    Task<IReadOnlyList<LedgerEntry>> GetLedgerAsync(Guid characterId, CancellationToken ct = default);
}

public class InventoryService(
    IInventoryRepository inventory,
    ICharacterRepository characters,
    ICurrentUser currentUser) : IInventoryService
{
    public async Task<IReadOnlyList<ItemInstance>> GetInventoryAsync(Guid characterId, CancellationToken ct = default)
    {
        await RequireCanViewAsync(characterId, ct);
        return await inventory.ListOwnedAsync(characterId, ct);
    }

    public async Task<IReadOnlyList<LedgerEntry>> GetLedgerAsync(Guid characterId, CancellationToken ct = default)
    {
        await RequireCanViewAsync(characterId, ct);
        return await inventory.ListLedgerAsync(characterId, ct);
    }

    /// <summary>Mirrors character-page visibility: the owning player, DMs, and CAs.</summary>
    private async Task RequireCanViewAsync(Guid characterId, CancellationToken ct)
    {
        var character = await characters.GetAsync(characterId, ct)
            ?? throw new NotFoundException(nameof(Character), characterId);

        if (character.OwnerUserId != currentUser.RequireUserId() && !currentUser.IsDm)
        {
            throw new ForbiddenAccessException("You can only view your own character's belongings.");
        }
    }
}

public interface IAuditService
{
    /// <summary>Campaign-wide transaction ledger with character names resolved. CA only.</summary>
    Task<IReadOnlyList<AuditRow>> ListAsync(AuditFilter filter, CancellationToken ct = default);
}

public record AuditRow(LedgerEntry Entry, string CharacterName, string? CounterpartyName);

public class AuditService(
    IInventoryRepository inventory,
    ICharacterRepository characters,
    ICurrentUser currentUser) : IAuditService
{
    public async Task<IReadOnlyList<AuditRow>> ListAsync(AuditFilter filter, CancellationToken ct = default)
    {
        if (!currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Campaign Admin role required for the audit ledger.");
        }

        var entries = await inventory.ListLedgerAllAsync(filter, ct);

        var characterIds = entries.Select(e => e.CharacterId)
            .Concat(entries.Where(e => e.CounterpartyCharacterId is not null).Select(e => e.CounterpartyCharacterId!.Value))
            .Distinct();
        var names = await characters.GetNamesAsync(characterIds, ct);

        return [.. entries.Select(e => new AuditRow(
            e,
            names.GetValueOrDefault(e.CharacterId, "(deleted)"),
            e.CounterpartyCharacterId is null ? null : names.GetValueOrDefault(e.CounterpartyCharacterId.Value, "(deleted)")))];
    }
}
