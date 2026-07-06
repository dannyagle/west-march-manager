namespace WestMarch.Domain.Adventures;

/// <summary>
/// Authoring lifecycle. Draft is visible to its author (and CAs);
/// ReadyForReview is visible to all DMs and CAs; only Approved adventures
/// can be selected for a session.
/// </summary>
public enum AdventureStatus
{
    Draft = 0,
    ReadyForReview = 1,
    Approved = 2,
}
