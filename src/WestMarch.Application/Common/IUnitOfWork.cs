namespace WestMarch.Application.Common;

/// <summary>Commits pending changes across repositories in one transaction.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}
