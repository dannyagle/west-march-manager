using WestMarch.Application.Common;
using WestMarch.Domain.Items;

namespace WestMarch.Application.Items;

public record CustomItemInput(
    ItemKind Kind,
    string Name,
    ItemRarity Rarity,
    string Category,
    bool RequiresAttunement,
    int? PriceGp,
    string? ExternalUrl);

/// <summary>CA edits to an existing item. For imported items only the override fields apply.</summary>
public record CatalogItemUpdate(
    int? CampaignPriceGp,
    bool IsActive,
    // Custom-item-only fields; ignored for imported items:
    string? Name = null,
    ItemRarity? Rarity = null,
    string? Category = null,
    bool? RequiresAttunement = null,
    string? ExternalUrl = null);

public record ImportChange(string Name, ItemRarity Rarity, string Detail);

public record ImportPreview(
    ItemKind Kind,
    IReadOnlyList<ImportChange> Added,
    IReadOnlyList<ImportChange> Updated,
    IReadOnlyList<ImportChange> Deactivated,
    int UnchangedCount);

public interface ICatalogService
{
    Task<IReadOnlyList<CatalogItem>> ListAsync(CatalogFilter filter, CancellationToken ct = default);
    Task<CatalogItem> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ImportBatch>> ListImportBatchesAsync(CancellationToken ct = default);

    Task<CatalogItem> CreateCustomAsync(CustomItemInput input, CancellationToken ct = default);
    Task<CatalogItem> UpdateAsync(Guid id, CatalogItemUpdate update, CancellationToken ct = default);

    Task<ImportPreview> PreviewImportAsync(ParsedCatalogFile file, CancellationToken ct = default);
    Task<ImportBatch> ApplyImportAsync(ParsedCatalogFile file, string fileName, CancellationToken ct = default);
}

/// <summary>
/// The item catalog: viewable by DMs, editable by CAs. Imports upsert by ImportKey and
/// deactivate items missing from the new file; custom items and CA overrides
/// (campaign price, active flag) are never touched by an import.
/// </summary>
public class CatalogService(
    ICatalogRepository catalog,
    IUnitOfWork uow,
    ICurrentUser currentUser) : ICatalogService
{
    public Task<IReadOnlyList<CatalogItem>> ListAsync(CatalogFilter filter, CancellationToken ct = default)
    {
        RequireDm();
        return catalog.ListAsync(filter, ct);
    }

    public async Task<CatalogItem> GetAsync(Guid id, CancellationToken ct = default)
    {
        RequireDm();
        return await catalog.GetAsync(id, ct) ?? throw new NotFoundException(nameof(CatalogItem), id);
    }

    public Task<IReadOnlyList<ImportBatch>> ListImportBatchesAsync(CancellationToken ct = default)
    {
        RequireDm();
        return catalog.ListBatchesAsync(ct);
    }

    public async Task<CatalogItem> CreateCustomAsync(CustomItemInput input, CancellationToken ct = default)
    {
        RequireCa();
        Validate(input);

        var item = new CatalogItem
        {
            Kind = input.Kind,
            Name = input.Name.Trim(),
            Rarity = input.Kind == ItemKind.Mundane ? ItemRarity.None : input.Rarity,
            Category = input.Category.Trim(),
            RequiresAttunement = input.RequiresAttunement,
            BasePriceGp = input.PriceGp,
            PriceRaw = input.PriceGp is null ? null : $"{input.PriceGp} GP",
            ExternalUrl = string.IsNullOrWhiteSpace(input.ExternalUrl) ? null : input.ExternalUrl.Trim(),
            Source = CatalogSource.Custom,
            ImportKey = null,
            CreatedByUserId = currentUser.RequireUserId(),
        };

        catalog.Add(item);
        await uow.SaveChangesAsync(ct);
        return item;
    }

    public async Task<CatalogItem> UpdateAsync(Guid id, CatalogItemUpdate update, CancellationToken ct = default)
    {
        RequireCa();

        var item = await catalog.GetAsync(id, ct) ?? throw new NotFoundException(nameof(CatalogItem), id);

        if (update.CampaignPriceGp is < 0)
        {
            throw new AppValidationException("A campaign price cannot be negative.");
        }

        item.CampaignPriceGp = update.CampaignPriceGp;
        item.IsActive = update.IsActive;

        if (item.Source == CatalogSource.Custom)
        {
            if (!string.IsNullOrWhiteSpace(update.Name))
            {
                item.Name = update.Name.Trim();
            }

            if (update.Rarity is not null && item.Kind == ItemKind.Magic)
            {
                item.Rarity = update.Rarity.Value;
            }

            if (update.Category is not null)
            {
                item.Category = update.Category.Trim();
            }

            if (update.RequiresAttunement is not null)
            {
                item.RequiresAttunement = update.RequiresAttunement.Value;
            }

            if (update.ExternalUrl is not null)
            {
                item.ExternalUrl = string.IsNullOrWhiteSpace(update.ExternalUrl) ? null : update.ExternalUrl.Trim();
            }
        }

        item.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
        return item;
    }

    public async Task<ImportPreview> PreviewImportAsync(ParsedCatalogFile file, CancellationToken ct = default)
    {
        RequireCa();

        var existing = await catalog.GetImportedByKeyAsync(file.Kind, ct);
        var incomingKeys = file.Items.Select(i => i.ImportKey).ToHashSet();

        var added = new List<ImportChange>();
        var updated = new List<ImportChange>();
        var unchanged = 0;

        foreach (var parsed in file.Items)
        {
            if (!existing.TryGetValue(parsed.ImportKey, out var current))
            {
                added.Add(new ImportChange(parsed.Name, parsed.Rarity, DescribePrice(parsed)));
            }
            else if (HasBaseFieldChanges(current, parsed))
            {
                updated.Add(new ImportChange(parsed.Name, parsed.Rarity, DescribeDiff(current, parsed)));
            }
            else
            {
                unchanged++;
            }
        }

        var deactivated = existing.Values
            .Where(e => e.IsActive && !incomingKeys.Contains(e.ImportKey!))
            .Select(e => new ImportChange(e.Name, e.Rarity, "no longer in the source file"))
            .OrderBy(c => c.Name)
            .ToList();

        return new ImportPreview(file.Kind, added, updated, deactivated, unchanged);
    }

    public async Task<ImportBatch> ApplyImportAsync(ParsedCatalogFile file, string fileName, CancellationToken ct = default)
    {
        RequireCa();

        if (file.Items.Count == 0)
        {
            throw new AppValidationException("The file contains no items — refusing to deactivate the whole catalog.");
        }

        var existing = await catalog.GetImportedByKeyAsync(file.Kind, ct);
        var incomingKeys = file.Items.Select(i => i.ImportKey).ToHashSet();

        var batch = new ImportBatch
        {
            Kind = file.Kind,
            FileName = fileName,
            SourceNote = file.SourceNote,
            UploadedByUserId = currentUser.RequireUserId(),
        };

        foreach (var parsed in file.Items)
        {
            if (existing.TryGetValue(parsed.ImportKey, out var current))
            {
                if (HasBaseFieldChanges(current, parsed))
                {
                    ApplyBaseFields(current, parsed);
                    current.LastImportBatchId = batch.Id;
                    current.UpdatedAt = DateTimeOffset.UtcNow;
                    batch.UpdatedCount++;
                }
                else
                {
                    batch.UnchangedCount++;
                }

                // Deliberately not resurrecting IsActive: a CA's retirement decision
                // outlives imports. Missing-from-file items are deactivated below.
            }
            else
            {
                var item = new CatalogItem
                {
                    Kind = file.Kind,
                    Source = CatalogSource.Imported,
                    ImportKey = parsed.ImportKey,
                    LastImportBatchId = batch.Id,
                };
                ApplyBaseFields(item, parsed);
                catalog.Add(item);
                batch.AddedCount++;
            }
        }

        foreach (var gone in existing.Values.Where(e => e.IsActive && !incomingKeys.Contains(e.ImportKey!)))
        {
            gone.IsActive = false;
            gone.UpdatedAt = DateTimeOffset.UtcNow;
            batch.DeactivatedCount++;
        }

        catalog.AddBatch(batch);
        await uow.SaveChangesAsync(ct);
        return batch;
    }

    private static void ApplyBaseFields(CatalogItem item, ParsedCatalogItem parsed)
    {
        item.Name = parsed.Name;
        item.Rarity = parsed.Rarity;
        item.Category = parsed.Category;
        item.RequiresAttunement = parsed.RequiresAttunement;
        item.BasePriceGp = parsed.BasePriceGp;
        item.PriceRaw = parsed.PriceRaw;
        item.PriceIsBasePlus = parsed.PriceIsBasePlus;
        item.ExternalUrl = parsed.ExternalUrl;
        item.DetailsJson = parsed.DetailsJson;
    }

    private static bool HasBaseFieldChanges(CatalogItem item, ParsedCatalogItem parsed) =>
        item.Name != parsed.Name
        || item.Category != parsed.Category
        || item.RequiresAttunement != parsed.RequiresAttunement
        || item.BasePriceGp != parsed.BasePriceGp
        || item.PriceRaw != parsed.PriceRaw
        || item.PriceIsBasePlus != parsed.PriceIsBasePlus
        || item.ExternalUrl != parsed.ExternalUrl
        || item.DetailsJson != parsed.DetailsJson;

    private static string DescribePrice(ParsedCatalogItem parsed) =>
        parsed.PriceRaw ?? (parsed.BasePriceGp is null ? "no price" : $"{parsed.BasePriceGp} gp");

    private static string DescribeDiff(CatalogItem current, ParsedCatalogItem parsed)
    {
        var notes = new List<string>();
        if (current.BasePriceGp != parsed.BasePriceGp || current.PriceRaw != parsed.PriceRaw)
        {
            notes.Add($"price {current.PriceRaw ?? "—"} → {parsed.PriceRaw ?? "—"}");
        }
        if (current.Category != parsed.Category)
        {
            notes.Add($"category {current.Category} → {parsed.Category}");
        }
        if (current.RequiresAttunement != parsed.RequiresAttunement)
        {
            notes.Add("attunement changed");
        }
        return notes.Count > 0 ? string.Join(", ", notes) : "details changed";
    }

    private static void Validate(CustomItemInput input)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors.Add("The item needs a name.");
        }

        if (input.Kind == ItemKind.Magic && input.Rarity == ItemRarity.None)
        {
            errors.Add("Magic items need a rarity.");
        }

        if (string.IsNullOrWhiteSpace(input.Category))
        {
            errors.Add("The item needs a category (e.g. Wondrous Item, Weapon).");
        }

        if (input.PriceGp is < 0)
        {
            errors.Add("Price cannot be negative.");
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException([.. errors]);
        }
    }

    private void RequireDm()
    {
        if (!currentUser.IsDm)
        {
            throw new ForbiddenAccessException("DM role required to view the catalog.");
        }
    }

    private void RequireCa()
    {
        if (!currentUser.IsCa)
        {
            throw new ForbiddenAccessException("Campaign Admin role required to modify the catalog.");
        }
    }
}
