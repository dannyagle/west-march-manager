using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WestMarch.Domain.Adventures;
using WestMarch.Domain.Announcements;
using WestMarch.Domain.Characters;
using WestMarch.Domain.Sessions;
using WestMarch.Infrastructure.Identity;

namespace WestMarch.Infrastructure.Persistence;

public class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> b)
    {
        b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        b.Property(c => c.Summary).HasMaxLength(200);
        b.Property(c => c.DdbUrl).HasMaxLength(300).IsRequired();
        b.Property(c => c.OwnerUserId).HasMaxLength(450).IsRequired();

        b.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(c => c.Credits)
            .WithOne(cr => cr.Character)
            .HasForeignKey(cr => cr.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(c => c.OwnerUserId);
    }
}

public class SessionCreditConfiguration : IEntityTypeConfiguration<SessionCredit>
{
    public void Configure(EntityTypeBuilder<SessionCredit> b)
    {
        // SessionId is a plain reference (no cascade) to avoid multiple cascade paths;
        // credits outlive their session as the character's permanent record.
        b.HasOne<GameSession>()
            .WithMany()
            .HasForeignKey(c => c.SessionId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(c => c.CharacterId);
    }
}

public class AdventureConfiguration : IEntityTypeConfiguration<Adventure>
{
    public void Configure(EntityTypeBuilder<Adventure> b)
    {
        b.Property(a => a.Title).HasMaxLength(150).IsRequired();
        b.Property(a => a.ShortDescription).HasMaxLength(500).IsRequired();
        b.Property(a => a.LongDescription).IsRequired();
        b.Property(a => a.AuthorUserId).HasMaxLength(450).IsRequired();
        b.Property(a => a.ApprovedByUserId).HasMaxLength(450);
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        b.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.AuthorUserId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasMany(a => a.Tags)
            .WithMany(t => t.Adventures)
            .UsingEntity("AdventureTags");

        b.HasMany(a => a.GuaranteedRewards)
            .WithOne()
            .HasForeignKey(r => r.AdventureId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(a => a.RewardOptionSets)
            .WithOne()
            .HasForeignKey(s => s.AdventureId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(a => a.Status);
    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.Property(t => t.Name).HasMaxLength(50).IsRequired();
        b.HasIndex(t => t.Name).IsUnique();
    }
}

public class RewardComponentConfiguration : IEntityTypeConfiguration<RewardComponent>
{
    public void Configure(EntityTypeBuilder<RewardComponent> b)
    {
        b.Property(r => r.Description).HasMaxLength(500).IsRequired();
        b.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
    }
}

public class RewardOptionSetConfiguration : IEntityTypeConfiguration<RewardOptionSet>
{
    public void Configure(EntityTypeBuilder<RewardOptionSet> b)
    {
        b.Property(s => s.Name).HasMaxLength(150).IsRequired();

        b.HasMany(s => s.Options)
            .WithOne()
            .HasForeignKey(o => o.RewardOptionSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RewardOptionConfiguration : IEntityTypeConfiguration<RewardOption>
{
    public void Configure(EntityTypeBuilder<RewardOption> b)
    {
        b.Property(o => o.Description).HasMaxLength(500).IsRequired();
        b.Property(o => o.ExternalUrl).HasMaxLength(500);
        // CatalogItemId stays a bare column until the catalog aggregate exists (deferred phase).
    }
}

public class GameSessionConfiguration : IEntityTypeConfiguration<GameSession>
{
    public void Configure(EntityTypeBuilder<GameSession> b)
    {
        b.ToTable("GameSessions");
        b.Property(s => s.DmUserId).HasMaxLength(450);
        b.Property(s => s.CreatedByUserId).HasMaxLength(450).IsRequired();
        b.Property(s => s.Location).HasMaxLength(200);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        b.HasOne(s => s.Adventure)
            .WithMany()
            .HasForeignKey(s => s.AdventureId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.DmUserId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasMany(s => s.Signups)
            .WithOne(su => su.Session)
            .HasForeignKey(su => su.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(s => s.Messages)
            .WithOne(m => m.Session)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(s => s.ScheduledAt);
        b.HasIndex(s => new { s.Status, s.DmUserId });

        b.Ignore(s => s.NeedsDm);
        b.Ignore(s => s.HasOpenSeats);
    }
}

public class SessionSignupConfiguration : IEntityTypeConfiguration<SessionSignup>
{
    public void Configure(EntityTypeBuilder<SessionSignup> b)
    {
        b.HasOne(su => su.Character)
            .WithMany()
            .HasForeignKey(su => su.CharacterId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(su => new { su.SessionId, su.CharacterId }).IsUnique();
    }
}

public class SessionMessageConfiguration : IEntityTypeConfiguration<SessionMessage>
{
    public void Configure(EntityTypeBuilder<SessionMessage> b)
    {
        b.Property(m => m.AuthorUserId).HasMaxLength(450).IsRequired();
        b.Property(m => m.Body).HasMaxLength(4000).IsRequired();

        b.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.AuthorUserId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(m => new { m.SessionId, m.PostedAt });
    }
}

public class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> b)
    {
        b.Property(a => a.Title).HasMaxLength(200).IsRequired();
        b.Property(a => a.Body).IsRequired();
        b.Property(a => a.AuthorUserId).HasMaxLength(450).IsRequired();

        b.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.AuthorUserId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(a => new { a.ActiveFrom, a.ActiveUntil });
    }
}
