namespace WestMarch.Application.Sessions;

/// <summary>What a posted board message looks like on the wire.</summary>
public record BoardMessage(Guid Id, Guid SessionId, string AuthorUserId, string AuthorName, string Body, DateTimeOffset PostedAt);

/// <summary>
/// Real-time fan-out seam. The application layer persists a message and hands it here;
/// the web layer implements this over SignalR (IHubContext). Keeps SignalR out of Application.
/// </summary>
public interface ISessionMessageBroadcaster
{
    Task BroadcastAsync(BoardMessage message, CancellationToken ct = default);
}
