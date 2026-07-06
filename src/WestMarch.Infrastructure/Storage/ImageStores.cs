using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using WestMarch.Application.Common;
using WestMarch.Application.Storage;

namespace WestMarch.Infrastructure.Storage;

public class ImageStoreOptions
{
    public const string SectionName = "ImageStore";

    /// <summary>"Local" (dev default) or "AzureBlob" (production).</summary>
    public string Provider { get; set; } = "Local";

    public LocalOptions Local { get; set; } = new();
    public AzureBlobOptions AzureBlob { get; set; } = new();

    public class LocalOptions
    {
        /// <summary>Physical directory for uploads. Defaulted to {webroot}/media at startup when empty.</summary>
        public string? RootPath { get; set; }

        /// <summary>Public URL prefix the files are served under.</summary>
        public string PublicPathPrefix { get; set; } = "/media";
    }

    public class AzureBlobOptions
    {
        public string ConnectionString { get; set; } = "";
        public string Container { get; set; } = "images";
    }
}

public abstract class ImageStoreBase
{
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
    };

    /// <summary>Random file name with an extension derived from the content type, never from user input.</summary>
    protected static string CreateSafeName(string contentType)
    {
        if (!AllowedTypes.TryGetValue(contentType, out var extension))
        {
            throw new AppValidationException("Only PNG, JPEG, GIF, or WebP images can be uploaded.");
        }

        return $"{Guid.NewGuid():N}{extension}";
    }
}

public class LocalDiskImageStore(IOptions<ImageStoreOptions> options) : ImageStoreBase, IImageStore
{
    public async Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var local = options.Value.Local;
        var root = local.RootPath
            ?? throw new InvalidOperationException("ImageStore:Local:RootPath is not configured.");

        Directory.CreateDirectory(root);

        var safeName = CreateSafeName(contentType);
        var path = Path.Combine(root, safeName);

        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);

        return $"{local.PublicPathPrefix.TrimEnd('/')}/{safeName}";
    }

    public Task DeleteAsync(string url, CancellationToken ct = default)
    {
        var local = options.Value.Local;
        var name = Path.GetFileName(url);

        if (local.RootPath is not null && name.Length > 0)
        {
            var path = Path.Combine(local.RootPath, name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }
}

public class AzureBlobImageStore(IOptions<ImageStoreOptions> options) : ImageStoreBase, IImageStore
{
    public async Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var container = await GetContainerAsync(ct);
        var safeName = CreateSafeName(contentType);
        var blob = container.GetBlobClient(safeName);

        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, ct);

        return blob.Uri.ToString();
    }

    public async Task DeleteAsync(string url, CancellationToken ct = default)
    {
        var container = await GetContainerAsync(ct);
        var name = Path.GetFileName(new Uri(url).LocalPath);
        await container.DeleteBlobIfExistsAsync(name, cancellationToken: ct);
    }

    private async Task<BlobContainerClient> GetContainerAsync(CancellationToken ct)
    {
        var cfg = options.Value.AzureBlob;
        var container = new BlobContainerClient(cfg.ConnectionString, cfg.Container);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);
        return container;
    }
}
