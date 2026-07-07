namespace WestMarch.Application.Common;

/// <summary>Commits pending changes across repositories in one transaction.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Like <see cref="SaveChangesAsync"/> but returns false on an optimistic-concurrency
    /// conflict instead of throwing a provider-specific exception — used by the marketplace
    /// so two buyers cannot purchase the same listing.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct = default);
}
