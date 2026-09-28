using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSurvey.Domain.Entities;

namespace SmartSurvey.Infrastructure.Persistence.Configurations;

/// <summary>Workspaces table (the tenants).</summary>
internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> b)
    {
        b.ToTable("Workspaces");
        b.Property(x => x.Name).HasMaxLength(Workspace.NameMaxLength).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(Workspace.SlugMaxLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.ContactEmail).HasMaxLength(256);
        b.Property(x => x.StatusReason).HasMaxLength(500);
        b.Ignore(x => x.IsActive);

        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => x.Status);
    }
}

/// <summary>Single-row system settings table.</summary>
internal sealed class PlatformSettingsConfiguration : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> b)
    {
        b.ToTable("PlatformSettings");
        b.Property(x => x.SupportEmail).HasMaxLength(256);
    }
}

/// <summary>
/// Foreign keys from the root tables to <see cref="Workspace"/>. Child tables (sections, questions,
/// answers …) carry <c>WorkspaceId</c> for filtering only; their consistency with the parent is
/// guaranteed by <see cref="AppDbContext"/>'s save guard. Deletes are restricted: removing a
/// workspace deletes its data explicitly, in order (see the platform workspace service).
/// </summary>
internal static class WorkspaceKeys
{
    /// <summary>Required workspace foreign key + index.</summary>
    public static void BelongsToWorkspace<T>(this EntityTypeBuilder<T> b)
        where T : class, Domain.Common.IWorkspaceOwned
    {
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.WorkspaceId);
    }
}
