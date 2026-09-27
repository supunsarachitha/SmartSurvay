using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.Infrastructure.Persistence.Configurations;

/// <summary>Responses table.</summary>
internal sealed class SurveyResponseConfiguration : IEntityTypeConfiguration<SurveyResponse>
{
    public void Configure(EntityTypeBuilder<SurveyResponse> b)
    {
        b.ToTable("Responses");
        b.Property(x => x.UserAgent).HasMaxLength(512);
        b.Ignore(x => x.Duration);

        // Deleting a user keeps their responses (anonymised).
        b.HasOne(x => x.Respondent).WithMany().HasForeignKey(x => x.RespondentId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.SurveyId, x.Status, x.SubmittedAt });
        b.HasIndex(x => new { x.RespondentId, x.SurveyId });

        b.HasMany(x => x.Answers).WithOne(a => a.Response).HasForeignKey(a => a.ResponseId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Answers table.</summary>
internal sealed class AnswerConfiguration : IEntityTypeConfiguration<Answer>
{
    public void Configure(EntityTypeBuilder<Answer> b)
    {
        b.ToTable("Answers");
        b.Property(x => x.TextValue).HasMaxLength(10_000);

        // Deleting a question from a survey design deletes its answers.
        b.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.ResponseId, x.QuestionId }).IsUnique();
        b.HasIndex(x => x.QuestionId);

        b.HasMany(x => x.Selections).WithOne(s => s.Answer).HasForeignKey(s => s.AnswerId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Answer selections table.</summary>
internal sealed class AnswerSelectionConfiguration : IEntityTypeConfiguration<AnswerSelection>
{
    public void Configure(EntityTypeBuilder<AnswerSelection> b)
    {
        b.ToTable("AnswerSelections");
        b.Property(x => x.FreeText).HasMaxLength(1000);
        b.HasOne(x => x.Option).WithMany().HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.AnswerId, x.OptionId }).IsUnique();
        b.HasIndex(x => x.OptionId);
    }
}

/// <summary>Identity user profile columns.</summary>
internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.DisplayName).HasMaxLength(200);
    }
}
