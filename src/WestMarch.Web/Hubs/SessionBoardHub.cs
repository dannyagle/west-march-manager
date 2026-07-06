using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WestMarch.Application.Sessions;

namespace WestMarch.Web.Hubs;

/// <summary>
/// Real-time fan-out for per-session message boards. Clients join a group per session
/// to receive new posts. Posting itself goes through ISessionService (service-layer
/// authorization + persistence), which broadcasts via <see cref="SignalRSessionMessageBroadcaster"/> —
/// the hub never writes.
/// </summary>
[Authorize]
public class SessionBoardHub : Hub
{
    public static string GroupName(Guid sessionId) => $"session-{sessionId}";

    public Task JoinSession(Guid sessionId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(sessionId));

    public Task LeaveSession(Guid sessionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(sessionId));
}

/// <summary>Implements the application-layer broadcast seam over SignalR.</summary>
public class SignalRSessionMessageBroadcaster(IHubContext<SessionBoardHub> hub) : ISessionMessageBroadcaster
{
    public Task BroadcastAsync(BoardMessage message, CancellationToken ct = default) =>
        hub.Clients.Group(SessionBoardHub.GroupName(message.SessionId))
            .SendAsync("MessagePosted", message, ct);
}
