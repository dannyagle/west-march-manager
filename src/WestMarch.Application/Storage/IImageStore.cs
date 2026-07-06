namespace WestMarch.Application.Storage;

/// <summary>
/// Stores uploaded images (announcements today; adventure art later) and returns a
/// publicly servable URL. Local disk in development, Azure Blob Storage in production.
/// </summary>
public interface IImageStore
{
    /// <summary>Saves the image and returns its public URL (relative or absolute).</summary>
    Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);

    Task DeleteAsync(string url, CancellationToken ct = default);
}
