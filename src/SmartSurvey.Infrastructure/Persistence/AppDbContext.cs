using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Common;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Identity;
using SmartSurvey.Infrastructure.Persistence.Converters;
using SmartSurvey.Infrastructure.Persistence.Encryption;

namespace SmartSurvey.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the whole application: ASP.NET Core Identity tables (GUID keys) plus the
/// survey, response, report and audit tables. Supports PostgreSQL (primary, migrations) and SQLite
/// (demos/tests, created with <c>EnsureCreated</c>).
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    /// <inheritdoc />
    public DbSet<Survey> Surveys => Set<Survey>();

    /// <inheritdoc />
    public DbSet<SurveySection> SurveySections => Set<SurveySection>();

    /// <inheritdoc />
    public DbSet<Question> Questions => Set<Question>();

    /// <inheritdoc />
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();

    /// <inheritdoc />
    public DbSet<LogicRule> LogicRules => Set<LogicRule>();

    /// <inheritdoc />
    public DbSet<LogicCondition> LogicConditions => Set<LogicCondition>();

    /// <inheritdoc />
    public DbSet<SurveyResponse> Responses => Set<SurveyResponse>();

    /// <inheritdoc />
    public DbSet<Answer> Answers => Set<Answer>();

    /// <inheritdoc />
    public DbSet<AnswerSelection> AnswerSelections => Set<AnswerSelection>();

    /// <inheritdoc />
    public DbSet<ReportDefinition> Reports => Set<ReportDefinition>();

    /// <inheritdoc />
    public DbSet<ReportWidget> ReportWidgets => Set<ReportWidget>();

    /// <inheritdoc />
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    /// <inheritdoc />
    public DbSet<BrandingSettings> BrandingSettings => Set<BrandingSettings>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Respondents' own words and browser details are encrypted at rest when a protector is configured
        // (FieldEncryption); choices, numbers and dates stay plain because reports aggregate them in the database.
        if (this.GetService<IDbContextOptions>().FindExtension<FieldEncryptionExtension>() is { } encryption)
        {
            // EF never passes nulls to converters, so one string converter serves the nullable columns.
            ValueConverter converter = new EncryptedStringConverter(encryption.Protector);
            builder.Entity<Answer>().Property(a => a.TextValue).HasConversion(converter);
            builder.Entity<AnswerSelection>().Property(s => s.FreeText).HasConversion(converter);
            builder.Entity<SurveyResponse>().Property(r => r.UserAgent).HasConversion(converter);
        }

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            // Keys are generated client-side (see Entity). ValueGeneratedNever makes EF treat new
            // entities discovered through navigations as Added (not Modified) during reconciliation.
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                builder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
            }

            // JSON settings columns use the binary jsonb type on PostgreSQL (text elsewhere).
            if (Database.IsNpgsql())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.GetValueConverter() is IJsonValueConverter)
                    {
                        property.SetColumnType("jsonb");
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // UTC everywhere; enums as readable strings (robust against enum re-ordering).
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
    }
}
