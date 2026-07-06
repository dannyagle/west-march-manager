using WestMarch.Application.Common;
using WestMarch.Domain.Announcements;

namespace WestMarch.Application.Announcements;

public record AnnouncementInput(
    string Title,
    string Body,
    DateTimeOffset ActiveFrom,
    DateTimeOffset? ActiveUntil,
    int? SortOrder);

public interface IAnnouncementService
{
    /// <summary>Public: announcements currently in their active window, for the main page.</summary>
    Task<IReadOnlyList<Announcement>> ListActiveAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Announcement>> ListAllAsync(CancellationToken ct = default);
    Task<Announcement> GetAsync(Guid id, CancellationToken ct = default);
    Task<Announcement> CreateAsync(AnnouncementInput input, CancellationToken ct = default);
    Task<Announcement> UpdateAsync(Guid id, AnnouncementInput input, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class AnnouncementService(
    IAnnouncementRepository announcements,
    IUnitOfWork uow,
    ICurrentUser currentUser) : IAnnouncementService
{
    public Task<IReadOnlyList<Announcement>> ListActiveAsync(CancellationToken ct = default) =>
        announcements.ListActiveAsync(DateTimeOffset.UtcNow, ct);

    public Task<IReadOnlyList<Announcement>> ListAllAsync(CancellationToken ct = default)
    {
        RequireCa();
        return announcements.ListAllAsync(ct);
    }

    public async Task<Announcement> GetAsync(Guid id, CancellationToken ct = default)
    {
        RequireCa();
        return await announcements.GetAsync(id, ct) ?? throw new NotFoundException(nameof(Announcement), id);
    }

    public async Task<Announcement> CreateAsync(AnnouncementInput input, CancellationToken ct = default)
    {
        RequireCa();
        Validate(input);

        var announcement = new Announcement
        {
            AuthorUserId = currentUser.RequireUserId(),
        };

        Apply(announcement, input);
        announcements.Add(announcement);
        await uow.SaveChangesAsync(ct);
        return announcement;
    }

    public async Task<Announcement> UpdateAsync(Guid id, AnnouncementInput input, CancellationToken ct = default)
    {
        RequireCa();
        Validate(input);

        var announcement = await announcements.GetAsync(id, ct)
            ?? throw new NotFoundException(nameof(Announcement), id);

        Apply(announcement, input);
        announcement.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
        return announcement;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        RequireCa();
        var announcement = await announcements.GetAsync(id, ct)
            ?? throw new NotFoundException(nameof(Announcement), id);

        announcements.Remove(announcement);
        await uow.SaveChangesAsync(ct);
    }

    private static void Apply(Announcement announcement, AnnouncementInput input)
    {
        announcement.Title = input.Title.Trim();
        announcement.Body = input.Body.Trim();
        announcement.ActiveFrom = input.ActiveFrom;
        announcement.ActiveUntil = input.ActiveUntil;
        announcement.SortOrder = input.SortOrder;
    }

    private void RequireCa()
    {
        if (!currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Campaign Admin role required.");
        }
    }

    private static void Validate(AnnouncementInput input)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors.Add("Title is required.");
        }

        if (string.IsNullOrWhiteSpace(input.Body))
        {
            errors.Add("Body is required.");
        }

        if (input.ActiveUntil is not null && input.ActiveUntil < input.ActiveFrom)
        {
            errors.Add("The display window must end after it starts.");
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException([.. errors]);
        }
    }
}
