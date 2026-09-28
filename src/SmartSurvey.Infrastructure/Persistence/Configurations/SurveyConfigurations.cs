using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.ValueObjects;
using SmartSurvey.Infrastructure.Persistence.Converters;

namespace SmartSurvey.Infrastructure.Persistence.Configurations;

/// <summary>Surveys table.</summary>
internal sealed class SurveyConfiguration : IEntityTypeConfiguration<Survey>
{
    public void Configure(EntityTypeBuilder<Survey> b)
    {
        b.ToTable("Surveys");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Slug).HasMaxLength(80).IsRequired();
        b.Property(x => x.WelcomeMessage).HasMaxLength(4000);
        b.Property(x => x.ThankYouMessage).HasMaxLength(4000);
        b.Property(x => x.AccessPasswordHash).HasMaxLength(200);
        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => new { x.Status, x.IsTemplate });
        b.HasIndex(x => x.CreatedAt);

        b.HasMany(x => x.Sections).WithOne(s => s.Survey).HasForeignKey(s => s.SurveyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Questions).WithOne(q => q.Survey).HasForeignKey(q => q.SurveyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.LogicRules).WithOne(r => r.Survey).HasForeignKey(r => r.SurveyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Responses).WithOne(r => r.Survey).HasForeignKey(r => r.SurveyId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Survey sections (pages).</summary>
internal sealed class SurveySectionConfiguration : IEntityTypeConfiguration<SurveySection>
{
    public void Configure(EntityTypeBuilder<SurveySection> b)
    {
        b.ToTable("SurveySections");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.HasIndex(x => new { x.SurveyId, x.Order });

        b.HasMany(x => x.Questions).WithOne(q => q.Section).HasForeignKey(q => q.SectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Questions.</summary>
internal sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> b)
    {
        b.ToTable("Questions");
        b.Property(x => x.Text).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Settings)
            .HasConversion(new JsonValueConverter<QuestionSettings>(), new JsonValueComparer<QuestionSettings>())
            .IsRequired();

        // Codes are unique per survey, enforced by the service (a unique index would break
        // code swaps within a single SaveChanges on PostgreSQL).
        b.HasIndex(x => new { x.SurveyId, x.Code });
        b.HasIndex(x => new { x.SectionId, x.Order });

        b.HasMany(x => x.Options).WithOne(o => o.Question).HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Question options.</summary>
internal sealed class QuestionOptionConfiguration : IEntityTypeConfiguration<QuestionOption>
{
    public void Configure(EntityTypeBuilder<QuestionOption> b)
    {
        b.ToTable("QuestionOptions");
        b.Property(x => x.Text).HasMaxLength(500).IsRequired();
        b.Property(x => x.Value).HasMaxLength(100);
        b.Property(x => x.FreeTextPlaceholder).HasMaxLength(200);
        b.HasIndex(x => new { x.QuestionId, x.Order });
    }
}

/// <summary>Logic rules.</summary>
internal sealed class LogicRuleConfiguration : IEntityTypeConfiguration<LogicRule>
{
    public void Configure(EntityTypeBuilder<LogicRule> b)
    {
        b.ToTable("LogicRules");
        b.HasOne(x => x.TargetQuestion).WithMany().HasForeignKey(x => x.TargetQuestionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.TargetSection).WithMany().HasForeignKey(x => x.TargetSectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Conditions).WithOne(c => c.LogicRule).HasForeignKey(c => c.LogicRuleId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Logic conditions.</summary>
internal sealed class LogicConditionConfiguration : IEntityTypeConfiguration<LogicCondition>
{
    public void Configure(EntityTypeBuilder<LogicCondition> b)
    {
        b.ToTable("LogicConditions");
        b.Property(x => x.Value).HasMaxLength(500);
        b.HasOne(x => x.SourceQuestion).WithMany().HasForeignKey(x => x.SourceQuestionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Option).WithMany().HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Cascade);
    }
}
