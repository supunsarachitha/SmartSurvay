using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Common;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Identity;
using SmartSurvey.Infrastructure.Persistence.Converters;

namespace SmartSurvey.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the whole application: ASP.NET Core Identity tables (GUID keys) plus the
/// workspace, survey, response, report and audit tables. Supports PostgreSQL (primary, migrations)
/// and SQLite (demos/tests, created with <c>EnsureCreated</c>).
/// </summary>
/// <remarks>
/// <para><b>Workspace isolation.</b> Every context has a <see cref="DataScope"/> (default
/// <see cref="DataScope.None"/>, i.e. fail-closed). Global query filters limit every
/// <see cref="IWorkspaceOwned"/> table and the audit log to the scope's workspace, and
/// <see cref="SaveChanges(bool)"/> stamps <c>WorkspaceId</c> on inserts and rejects writes that
/// would touch another workspace. Identity tables and <see cref="Workspaces"/> are not filtered
/// (sign-in needs global lookups); services filter them explicitly.</para>
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    // Read by the global query filters; EF Core evaluates context members per instance.
    private Guid _filterWorkspaceId;
    private bool _unfiltered;

    /// <summary>Current data scope (see <see cref="UseScope"/>).</summary>
    public DataScope Scope { get; private set; }

    /// <summary>Sets the data scope. Call right after creating the context, before the first query.</summary>
    public AppDbContext UseScope(DataScope scope)
    {
        Scope = scope;
        _filterWorkspaceId = scope.Kind == DataScopeKind.Workspace ? scope.WorkspaceId : Guid.Empty;
        _unfiltered = scope.Kind == DataScopeKind.System;
        return this;
    }

    /// <inheritdoc />
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    /// <inheritdoc />
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();

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

        // Workspace isolation (see remarks on the class). Guid.Empty never matches a row, so the
        // None scope sees nothing.
        foreach (var type in builder.Model.GetEntityTypes().Select(t => t.ClrType).Where(typeof(IWorkspaceOwned).IsAssignableFrom).ToList())
        {
            ApplyWorkspaceFilterMethod.MakeGenericMethod(type).Invoke(this, [builder]);
        }

        builder.Entity<AuditLogEntry>().HasQueryFilter(e => _unfiltered || e.WorkspaceId == _filterWorkspaceId);

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
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWorkspaceRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyWorkspaceRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static readonly MethodInfo ApplyWorkspaceFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyWorkspaceFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private void ApplyWorkspaceFilter<TEntity>(ModelBuilder builder)
        where TEntity : class, IWorkspaceOwned =>
        builder.Entity<TEntity>().HasQueryFilter(e => _unfiltered || e.WorkspaceId == _filterWorkspaceId);

    /// <summary>
    /// Stamps and guards <c>WorkspaceId</c> of pending changes. Violations are programming errors
    /// (a service used the wrong scope), so they throw <see cref="InvalidOperationException"/>.
    /// </summary>
    private void ApplyWorkspaceRules()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case IWorkspaceOwned owned:
                    Guard(entry.Metadata.ClrType.Name, entry.State, owned.WorkspaceId,
                        entry.Property(nameof(IWorkspaceOwned.WorkspaceId)).IsModified, id => owned.WorkspaceId = id, allowUnowned: false);
                    break;
                case AuditLogEntry audit:
                    Guard(nameof(AuditLogEntry), entry.State, audit.WorkspaceId,
                        entry.Property(nameof(AuditLogEntry.WorkspaceId)).IsModified, id => audit.WorkspaceId = id, allowUnowned: true);
                    break;
            }
        }
    }

    /// <param name="entityName">Entity type name (for the error message).</param>
    /// <param name="state">Pending change.</param>
    /// <param name="workspaceId">Current value (null/empty = not set).</param>
    /// <param name="workspaceChanged">True when an existing row's workspace is being changed.</param>
    /// <param name="stamp">Sets the workspace on an insert.</param>
    /// <param name="allowUnowned">True for audit entries: rows without a workspace are system events.</param>
    private void Guard(string entityName, EntityState state, Guid? workspaceId, bool workspaceChanged, Action<Guid> stamp, bool allowUnowned)
    {
        var owner = workspaceId is { } id && id != Guid.Empty ? id : (Guid?)null;
        if (state != EntityState.Added && workspaceChanged)
        {
            throw new InvalidOperationException($"{entityName}: the workspace of existing data cannot be changed.");
        }

        switch (Scope.Kind)
        {
            case DataScopeKind.Workspace when owner is null && state == EntityState.Added:
                stamp(Scope.WorkspaceId);
                break;
            case DataScopeKind.Workspace when owner != Scope.WorkspaceId:
                throw new InvalidOperationException($"{entityName}: cannot write data of another workspace ({Scope}).");
            case DataScopeKind.System when owner is null && !allowUnowned:
                throw new InvalidOperationException($"{entityName}: system-scope inserts must set WorkspaceId explicitly.");
            case DataScopeKind.None when owner is not null || !allowUnowned || state != EntityState.Added:
                throw new InvalidOperationException($"{entityName}: workspace data cannot be written without a workspace scope.");
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
