using WestMarch.Domain.Announcements;

namespace WestMarch.Application.Announcements;

public interface IAnnouncementRepository
{
    Task<Announcement?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Entries active on the given date, ordered by SortOrder (nulls last) then newest first.</summary>
    Task<IReadOnlyList<Announcement>> ListActiveAsync(DateTimeOffset onDate, CancellationToken ct = default);

    /// <summary>All entries for the CA management screen.</summary>
    Task<IReadOnlyList<Announcement>> ListAllAsync(CancellationToken ct = default);

    void Add(Announcement announcement);
    void Remove(Announcement announcement);
}
