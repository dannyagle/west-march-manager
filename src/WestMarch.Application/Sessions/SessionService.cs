using WestMarch.Application.Adventures;
using WestMarch.Application.Characters;
using WestMarch.Application.Common;
using WestMarch.Application.Users;
using WestMarch.Domain.Adventures;
using WestMarch.Domain.Characters;
using WestMarch.Domain.Sessions;

namespace WestMarch.Application.Sessions;

public record SessionInput(Guid AdventureId, DateTimeOffset ScheduledAt, string? Location, bool VolunteerAsDm);

/// <summary>Soft warnings surfaced on signup/creation — flagged, never hard-blocked.</summary>
public record SignupWarning(string Message);

public interface ISessionService
{
    Task<IReadOnlyList<GameSession>> ListScheduledAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default);
    Task<IReadOnlyList<GameSession>> ListNeedingDmAsync(CancellationToken ct = default);
    Task<GameSession> GetAsync(Guid id, CancellationToken ct = default);

    Task<GameSession> CreateAsync(SessionInput input, CancellationToken ct = default);
    Task CancelAsync(Guid sessionId, CancellationToken ct = default);

    Task ClaimDmSeatAsync(Guid sessionId, CancellationToken ct = default);
    Task ReleaseDmSeatAsync(Guid sessionId, CancellationToken ct = default);

    Task<IReadOnlyList<SignupWarning>> GetSignupWarningsAsync(Guid sessionId, Guid characterId, CancellationToken ct = default);
    Task SignUpAsync(Guid sessionId, Guid characterId, CancellationToken ct = default);
    Task WithdrawAsync(Guid sessionId, Guid characterId, CancellationToken ct = default);

    /// <summary>DM marks the session complete, checking off which characters succeeded and earn a credit.</summary>
    Task CompleteAsync(Guid sessionId, IReadOnlyCollection<Guid> successfulCharacterIds, CancellationToken ct = default);

    Task<IReadOnlyList<SessionMessage>> GetMessagesAsync(Guid sessionId, CancellationToken ct = default);
    Task<BoardMessage> PostMessageAsync(Guid sessionId, string body, CancellationToken ct = default);
}

public class SessionService(
    ISessionRepository sessions,
    IAdventureRepository adventures,
    ICharacterRepository characters,
    IUserDirectory userDirectory,
    ISessionMessageBroadcaster broadcaster,
    IUnitOfWork uow,
    ICurrentUser currentUser) : ISessionService
{
    public Task<IReadOnlyList<GameSession>> ListScheduledAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default) =>
        sessions.ListScheduledAsync(from, until, ct);

    public async Task<IReadOnlyList<GameSession>> ListNeedingDmAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsDm)
        {
            throw new ForbiddenAccessException("DM role required.");
        }

        return await sessions.ListNeedingDmAsync(DateTimeOffset.UtcNow.AddHours(-6), ct);
    }

    public async Task<GameSession> GetAsync(Guid id, CancellationToken ct = default)
    {
        currentUser.RequireUserId();
        var session = await sessions.GetReadOnlyAsync(id, ct)
            ?? throw new NotFoundException("Session", id);

        // DM-only adventure fields stay server-side for non-DMs.
        if (!currentUser.IsDm && session.Adventure is not null)
        {
            session.Adventure.DmNotes = null;
            session.Adventure.MonsterStatBlocks = null;
        }

        return session;
    }

    public async Task<GameSession> CreateAsync(SessionInput input, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();

        if (input.ScheduledAt < DateTimeOffset.UtcNow)
        {
            throw new AppValidationException("Sessions must be scheduled in the future.");
        }

        var adventure = await adventures.GetReadOnlyAsync(input.AdventureId, ct)
            ?? throw new NotFoundException(nameof(Adventure), input.AdventureId);

        if (adventure.Status != AdventureStatus.Approved)
        {
            throw new AppValidationException("Only approved adventures can be scheduled.");
        }

        if (!adventure.IsActiveOn(input.ScheduledAt))
        {
            throw new AppValidationException("The adventure is not active on the chosen date.");
        }

        if (input.VolunteerAsDm && !currentUser.IsDm)
        {
            throw new ForbiddenAccessException("Only DMs can take the DM seat.");
        }

        var session = new GameSession
        {
            AdventureId = adventure.Id,
            ScheduledAt = input.ScheduledAt,
            Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim(),
            CreatedByUserId = userId,
            DmUserId = input.VolunteerAsDm ? userId : null,
        };

        sessions.Add(session);
        await uow.SaveChangesAsync(ct);
        return session;
    }

    public async Task CancelAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await GetForMutationAsync(sessionId, ct);
        var userId = currentUser.RequireUserId();

        var mayCancel = currentUser.IsCa || session.CreatedByUserId == userId || session.DmUserId == userId;
        if (!mayCancel)
        {
            throw new ForbiddenAccessException("Only the session's creator, its DM, or a Campaign Admin can cancel it.");
        }

        if (session.Status != SessionStatus.Scheduled)
        {
            throw new AppValidationException("Only scheduled sessions can be cancelled.");
        }

        session.Status = SessionStatus.Cancelled;
        await uow.SaveChangesAsync(ct);
    }

    public async Task ClaimDmSeatAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (!currentUser.IsDm)
        {
            throw new ForbiddenAccessException("DM role required to claim the DM seat.");
        }

        var session = await GetForMutationAsync(sessionId, ct);

        if (session.Status != SessionStatus.Scheduled)
        {
            throw new AppValidationException("This session is no longer scheduled.");
        }

        if (session.DmUserId is not null)
        {
            throw new AppValidationException("This session already has a DM.");
        }

        session.DmUserId = currentUser.RequireUserId();
        await uow.SaveChangesAsync(ct);
    }

    public async Task ReleaseDmSeatAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await GetForMutationAsync(sessionId, ct);

        if (session.DmUserId != currentUser.RequireUserId() && !currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Only the assigned DM or a Campaign Admin can release the DM seat.");
        }

        session.DmUserId = null;
        await uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SignupWarning>> GetSignupWarningsAsync(Guid sessionId, Guid characterId, CancellationToken ct = default)
    {
        var session = await sessions.GetReadOnlyAsync(sessionId, ct)
            ?? throw new NotFoundException("Session", sessionId);
        var character = await characters.GetWithHistoryAsync(characterId, ct)
            ?? throw new NotFoundException(nameof(Character), characterId);

        return GetWarnings(session, character);
    }

    public async Task SignUpAsync(Guid sessionId, Guid characterId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var session = await GetForMutationAsync(sessionId, ct);

        if (session.Status != SessionStatus.Scheduled)
        {
            throw new AppValidationException("Signups are closed — this session is no longer scheduled.");
        }

        var character = await characters.GetAsync(characterId, ct)
            ?? throw new NotFoundException(nameof(Character), characterId);

        if (character.OwnerUserId != userId)
        {
            throw new ForbiddenAccessException("You can only sign up your own characters.");
        }

        if (character.IsRetired)
        {
            throw new AppValidationException($"{character.Name} is retired.");
        }

        if (session.Signups.Any(s => s.CharacterId == characterId))
        {
            throw new AppValidationException($"{character.Name} is already signed up.");
        }

        if (session.Signups.Any(s => s.Character.OwnerUserId == userId))
        {
            throw new AppValidationException("You already have a character in this session.");
        }

        // Level-range and headcount mismatches are deliberately soft: the UI surfaces
        // GetSignupWarningsAsync results, and the DM sees mismatch badges on the roster.
        var signup = new SessionSignup
        {
            SessionId = session.Id,
            CharacterId = character.Id,
        };

        // Registered through the repository only: the change tracker's relationship
        // fixup also places it in session.Signups, so adding it there too would duplicate it.
        sessions.AddSignup(signup);

        await uow.SaveChangesAsync(ct);
    }

    public async Task WithdrawAsync(Guid sessionId, Guid characterId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var session = await GetForMutationAsync(sessionId, ct);

        var signup = session.Signups.FirstOrDefault(s => s.CharacterId == characterId)
            ?? throw new NotFoundException(nameof(SessionSignup), characterId);

        // Players withdraw their own characters; the session DM or a CA can drop anyone.
        var mayWithdraw = signup.Character.OwnerUserId == userId
            || session.DmUserId == userId
            || currentUser.IsCa;

        if (!mayWithdraw)
        {
            throw new ForbiddenAccessException("You cannot withdraw someone else's character.");
        }

        if (session.Status != SessionStatus.Scheduled)
        {
            throw new AppValidationException("This session is no longer scheduled.");
        }

        session.Signups.Remove(signup);
        await uow.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(Guid sessionId, IReadOnlyCollection<Guid> successfulCharacterIds, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var session = await GetForMutationAsync(sessionId, ct);

        if (session.DmUserId != userId && !currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Only the session's DM or a Campaign Admin can complete it.");
        }

        if (session.Status != SessionStatus.Scheduled)
        {
            throw new AppValidationException("This session has already been completed or cancelled.");
        }

        var unknown = successfulCharacterIds.Where(id => session.Signups.All(s => s.CharacterId != id)).ToList();
        if (unknown.Count > 0)
        {
            throw new AppValidationException("Credit can only be awarded to characters signed up for this session.");
        }

        foreach (var signup in session.Signups)
        {
            var succeeded = successfulCharacterIds.Contains(signup.CharacterId);
            signup.ReceivedCredit = succeeded;

            if (!succeeded)
            {
                continue;
            }

            var character = await characters.GetAsync(signup.CharacterId, ct)
                ?? throw new NotFoundException(nameof(Character), signup.CharacterId);

            var result = LevelingEngine.AwardSessionCredit(character);

            characters.AddCredit(new SessionCredit
            {
                CharacterId = character.Id,
                SessionId = session.Id,
                LevelAtAward = result.LevelAtAward,
                TriggeredLevelUp = result.TriggeredLevelUp,
            });
        }

        session.Status = SessionStatus.Completed;
        session.CompletedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SessionMessage>> GetMessagesAsync(Guid sessionId, CancellationToken ct = default)
    {
        currentUser.RequireUserId();
        return await sessions.ListMessagesAsync(sessionId, ct);
    }

    public async Task<BoardMessage> PostMessageAsync(Guid sessionId, string body, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new AppValidationException("A message needs some words.");
        }

        var session = await sessions.GetReadOnlyAsync(sessionId, ct)
            ?? throw new NotFoundException("Session", sessionId);

        // Posting is for participants: signed-up players, the DM, the creator, or a CA.
        var isParticipant = session.DmUserId == userId
            || session.CreatedByUserId == userId
            || session.Signups.Any(s => s.Character.OwnerUserId == userId)
            || currentUser.IsCa;

        if (!isParticipant)
        {
            throw new ForbiddenAccessException("Join the session to post on its board.");
        }

        var message = new SessionMessage
        {
            SessionId = sessionId,
            AuthorUserId = userId,
            Body = body.Trim(),
        };

        sessions.AddMessage(message);
        await uow.SaveChangesAsync(ct);

        var names = await userDirectory.GetDisplayNamesAsync([userId], ct);
        var wire = new BoardMessage(
            message.Id, sessionId, userId,
            names.GetValueOrDefault(userId, "Adventurer"),
            message.Body, message.PostedAt);

        await broadcaster.BroadcastAsync(wire, ct);
        return wire;
    }

    private async Task<GameSession> GetForMutationAsync(Guid sessionId, CancellationToken ct) =>
        await sessions.GetAsync(sessionId, ct) ?? throw new NotFoundException("Session", sessionId);

    private static List<SignupWarning> GetWarnings(GameSession session, Character character)
    {
        var warnings = new List<SignupWarning>();
        var adventure = session.Adventure;

        if (!adventure.IsLevelInRange(character.Level))
        {
            warnings.Add(new SignupWarning(
                $"{character.Name} is level {character.Level}, outside this adventure's level {adventure.MinLevel}–{adventure.MaxLevel} range."));
        }

        if (session.Signups.Count >= adventure.TargetPlayersMax)
        {
            warnings.Add(new SignupWarning(
                $"The table is already at its target of {adventure.TargetPlayersMax} characters."));
        }

        return warnings;
    }
}
