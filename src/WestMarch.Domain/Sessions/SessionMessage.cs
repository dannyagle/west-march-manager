namespace WestMarch.Domain.Sessions;

/// <summary>A message on a session's real-time board.</summary>
public class SessionMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }
    public GameSession Session { get; set; } = default!;

    public string AuthorUserId { get; set; } = default!;

    public string Body { get; set; } = default!;

    public DateTimeOffset PostedAt { get; set; } = DateTimeOffset.UtcNow;
}
