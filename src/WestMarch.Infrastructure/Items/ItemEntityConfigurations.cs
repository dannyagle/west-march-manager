using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WestMarch.Domain.Characters;
using WestMarch.Domain.Items;

namespace WestMarch.Infrastructure.Items;

public class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> b)
    {
        b.Property(c => c.Name).HasMaxLength(200).IsRequired();
        b.Property(c => c.Category).HasMaxLength(100);
        b.Property(c => c.PriceRaw).HasMaxLength(100);
        b.Property(c => c.ExternalUrl).HasMaxLength(500);
        b.Property(c => c.ImportKey).HasMaxLength(250);
        b.Property(c => c.CreatedByUserId).HasMaxLength(450);
        b.Property(c => c.Kind).HasConversion<string>().HasMaxLength(10);
        b.Property(c => c.Rarity).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Source).HasConversion<string>().HasMaxLength(10);

        b.Ignore(c => c.EffectivePriceGp);
        b.Ignore(c => c.IsSellable);

        b.HasIndex(c => c.ImportKey);
        b.HasIndex(c => new { c.Kind, c.IsActive });
    }
}

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.SourceNote).HasMaxLength(500);
        b.Property(x => x.UploadedByUserId).HasMaxLength(450).IsRequired();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
    }
}

public class ItemInstanceConfiguration : IEntityTypeConfiguration<ItemInstance>
{
    public void Configure(EntityTypeBuilder<ItemInstance> b)
    {
        b.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);

        // Catalog items are never hard-deleted (imports deactivate), so Restrict is safe.
        b.HasOne(i => i.CatalogItem)
            .WithMany()
            .HasForeignKey(i => i.CatalogItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // If a character is ever deleted, instances become ownerless history rather than blocking the delete.
        b.HasOne<Character>()
            .WithMany()
            .HasForeignKey(i => i.OwnerCharacterId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(i => new { i.OwnerCharacterId, i.Status });
    }
}

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.Property(l => l.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(l => l.ItemName).HasMaxLength(200);
        b.Property(l => l.Description).HasMaxLength(600).IsRequired();

        // Deliberately no FK on CharacterId/CounterpartyCharacterId: the ledger is an
        // append-only audit trail that must outlive anything it references.
        b.HasIndex(l => new { l.CharacterId, l.OccurredAt });
        b.HasIndex(l => l.OccurredAt);
    }
}

public class MarketListingConfiguration : IEntityTypeConfiguration<MarketListing>
{
    public void Configure(EntityTypeBuilder<MarketListing> b)
    {
        b.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);

        // Two concurrent buyers: the second commit sees a changed Version and fails.
        b.Property(l => l.Version).IsConcurrencyToken();

        b.HasOne(l => l.ItemInstance)
            .WithMany()
            .HasForeignKey(l => l.ItemInstanceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Seller/Buyer character ids are bare references (no FK) for the same
        // audit-outlives-everything reason as the ledger.
        b.HasIndex(l => l.Status);
        b.HasIndex(l => l.ItemInstanceId);
    }
}
