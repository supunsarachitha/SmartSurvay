using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSurvey.Domain.Entities;

namespace SmartSurvey.Infrastructure.Persistence.Configurations;

/// <summary>Single-row branding table.</summary>
internal sealed class BrandingSettingsConfiguration : IEntityTypeConfiguration<BrandingSettings>
{
    public void Configure(EntityTypeBuilder<BrandingSettings> b)
    {
        b.ToTable("BrandingSettings");
        b.Property(x => x.ProductName).HasMaxLength(80).IsRequired();
        b.Property(x => x.Tagline).HasMaxLength(200);
        b.Property(x => x.IconName).HasMaxLength(60).IsRequired();
        b.Property(x => x.LogoContentType).HasMaxLength(100);
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}
