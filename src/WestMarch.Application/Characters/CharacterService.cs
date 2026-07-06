using WestMarch.Application.Common;
using WestMarch.Domain.Characters;

namespace WestMarch.Application.Characters;

public record CharacterInput(string Name, string? Summary, string DdbUrl);

public interface ICharacterService
{
    Task<IReadOnlyList<Character>> GetMyCharactersAsync(bool includeRetired = false, CancellationToken ct = default);
    Task<Character> GetAsync(Guid id, CancellationToken ct = default);
    Task<Character> CreateAsync(CharacterInput input, CancellationToken ct = default);
    Task<Character> UpdateAsync(Guid id, CharacterInput input, CancellationToken ct = default);
    Task RetireAsync(Guid id, bool retired, CancellationToken ct = default);
}

public class CharacterService(
    ICharacterRepository characters,
    IUnitOfWork uow,
    ICurrentUser currentUser) : ICharacterService
{
    public Task<IReadOnlyList<Character>> GetMyCharactersAsync(bool includeRetired = false, CancellationToken ct = default) =>
        characters.ListByOwnerAsync(currentUser.RequireUserId(), includeRetired, ct);

    public async Task<Character> GetAsync(Guid id, CancellationToken ct = default)
    {
        var character = await characters.GetWithHistoryAsync(id, ct)
            ?? throw new NotFoundException(nameof(Character), id);

        // Owners see their own characters; DMs and CAs may inspect any character
        // (they need it for session rosters and administration).
        if (character.OwnerUserId != currentUser.RequireUserId() && !currentUser.IsDm)
        {
            throw new ForbiddenAccessException("You can only view your own characters.");
        }

        return character;
    }

    public async Task<Character> CreateAsync(CharacterInput input, CancellationToken ct = default)
    {
        var ownerId = currentUser.RequireUserId();
        Validate(input);

        var character = new Character
        {
            OwnerUserId = ownerId,
            Name = input.Name.Trim(),
            Summary = string.IsNullOrWhiteSpace(input.Summary) ? null : input.Summary.Trim(),
            Level = LevelingEngine.StartingLevel,
            DdbUrl = input.DdbUrl.Trim(),
            DdbCharacterId = DdbUrlParser.TryGetCharacterId(input.DdbUrl),
        };

        characters.Add(character);
        await uow.SaveChangesAsync(ct);
        return character;
    }

    public async Task<Character> UpdateAsync(Guid id, CharacterInput input, CancellationToken ct = default)
    {
        var character = await GetOwnedAsync(id, ct);
        Validate(input);

        character.Name = input.Name.Trim();
        character.Summary = string.IsNullOrWhiteSpace(input.Summary) ? null : input.Summary.Trim();
        character.DdbUrl = input.DdbUrl.Trim();
        character.DdbCharacterId = DdbUrlParser.TryGetCharacterId(input.DdbUrl);

        await uow.SaveChangesAsync(ct);
        return character;
    }

    public async Task RetireAsync(Guid id, bool retired, CancellationToken ct = default)
    {
        var character = await GetOwnedAsync(id, ct);
        character.IsRetired = retired;
        await uow.SaveChangesAsync(ct);
    }

    private async Task<Character> GetOwnedAsync(Guid id, CancellationToken ct)
    {
        var character = await characters.GetAsync(id, ct)
            ?? throw new NotFoundException(nameof(Character), id);

        if (character.OwnerUserId != currentUser.RequireUserId() && !currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Only the owning player can modify this character.");
        }

        return character;
    }

    private static void Validate(CharacterInput input)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors.Add("A character needs a name.");
        }

        if (!DdbUrlParser.IsValid(input.DdbUrl))
        {
            errors.Add("A valid D&D Beyond character link is required (https://www.dndbeyond.com/characters/…).");
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException([.. errors]);
        }
    }
}
