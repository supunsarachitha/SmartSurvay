using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Users;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Infrastructure.Email;
using SmartSurvey.Infrastructure.Exports;
using SmartSurvey.Infrastructure.Identity;
using SmartSurvey.Infrastructure.Persistence;
using SmartSurvey.Infrastructure.Persistence.Encryption;
using SmartSurvey.Infrastructure.Persistence.Seed;
using SmartSurvey.Infrastructure.Workspaces;
using SmartSurvey.Infrastructure.Security;

namespace SmartSurvey.Infrastructure;

/// <summary>Registers Infrastructure-layer services.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds the EF Core context (PostgreSQL or SQLite, see <see cref="DatabaseOptions"/>), the
    /// context factory, exporters, e-mail delivery, user administration, seeding and health checks.
    /// Requires an <see cref="ICurrentUser"/> registration (provided by the Web host).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<BrandingOptions>(configuration.GetSection(BrandingOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<FieldEncryptionOptions>(configuration.GetSection(FieldEncryptionOptions.SectionName));
        services.AddSingleton<IFieldProtector, DataProtectionFieldProtector>();
        services.AddScoped<FieldEncryptionMigrator>();

        services.AddScoped<AuditableEntityInterceptor>();

        // Scoped factory lifetime lets the options action resolve the scoped interceptor/current user.
        // Provider and connection string are read lazily so test hosts can override configuration.
        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            var dbOptions = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            if (dbOptions.Provider == DatabaseProvider.Sqlite)
            {
                options.UseSqlite(connectionString);
            }
            else
            {
                // No retrying execution strategy: it forbids user-initiated transactions, which the
                // services use for atomic operations (e.g. quota-checked submissions).
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
            }

            options.AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>());

            // Encrypt respondents' free-text answers, "Other" texts and browser details at rest (see FieldEncryption).
            if (sp.GetRequiredService<IOptions<FieldEncryptionOptions>>().Value.Enabled)
            {
                options.UseFieldEncryption(sp.GetRequiredService<IFieldProtector>());
            }
        }, ServiceLifetime.Scoped);

        services.AddScoped<IAppDbContextFactory, AppDbContextFactory>();

        // Exporters are stateless; the report service picks one by ExportFormat (PdfReportExporter
        // configures QuestPDF itself).
        services.AddSingleton<IReportExporter, PdfReportExporter>();
        services.AddSingleton<IReportExporter, CsvReportExporter>();
        services.AddSingleton<IReportExporter, TxtReportExporter>();
        services.AddSingleton<IReportExporter, XlsxReportExporter>();
        services.AddSingleton<IReportExporter, JsonReportExporter>();
        services.AddScoped<IResponseExportService, ResponseExportService>();

        services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        services.AddSingleton<ISurveyAccessKeys, DataProtectionSurveyAccessKeys>();
        services.Configure<BotProtectionOptions>(configuration.GetSection(BotProtectionOptions.SectionName));
        services.AddSingleton<IBotProtection, ProofOfWorkBotProtection>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IPlatformWorkspaceService, PlatformWorkspaceService>();
        services.AddScoped<IWorkspaceSignupService, WorkspaceSignupService>();
        services.AddScoped<DbSeeder>();

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

        return services;
    }
}
