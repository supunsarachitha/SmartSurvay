using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.ValueObjects;
using SmartSurvey.Infrastructure.Persistence.Converters;

namespace SmartSurvey.Infrastructure.Persistence.Configurations;

/// <summary>Report definitions.</summary>
internal sealed class ReportDefinitionConfiguration : IEntityTypeConfiguration<ReportDefinition>
{
    public void Configure(EntityTypeBuilder<ReportDefinition> b)
    {
        b.ToTable("Reports");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Filters)
            .HasConversion(new JsonValueConverter<ReportFilterSet>(), new JsonValueComparer<ReportFilterSet>())
            .IsRequired();

        b.HasOne(x => x.Survey).WithMany().HasForeignKey(x => x.SurveyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Widgets).WithOne(w => w.Report).HasForeignKey(w => w.ReportId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.SurveyId);
    }
}

/// <summary>Report widgets.</summary>
internal sealed class ReportWidgetConfiguration : IEntityTypeConfiguration<ReportWidget>
{
    public void Configure(EntityTypeBuilder<ReportWidget> b)
    {
        b.ToTable("ReportWidgets");
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Settings)
            .HasConversion(new JsonValueConverter<WidgetSettings>(), new JsonValueComparer<WidgetSettings>())
            .IsRequired();

        // A widget survives deletion of its question; the engine reports "question removed".
        b.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.SecondaryQuestion).WithMany().HasForeignKey(x => x.SecondaryQuestionId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.ReportId, x.Order });
    }
}

/// <summary>Audit log.</summary>
internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.ToTable("AuditLogs");
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(100);
        b.Property(x => x.UserName).HasMaxLength(256);
        b.Property(x => x.Details).HasMaxLength(4000);
        b.HasIndex(x => x.Timestamp);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
