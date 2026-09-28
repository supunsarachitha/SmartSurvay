using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys.Validation;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Surveys;

/// <summary>
/// Survey design use cases for administrators: listing, loading, creating, editing, deleting,
/// lifecycle changes, duplication and JSON import/export of survey definitions.
/// </summary>
/// <remarks>
/// <para>Every operation is admin-only (defense in depth behind the UI/API authorization policies)
/// and uses one short-lived database context. Incoming designs are never modified: the service
/// works on a private copy that is normalised (<see cref="SurveyNormalizer"/>), validated
/// (<see cref="SurveyDefinitionValidator"/>) and then persisted.</para>
/// <para>Archived surveys are read-only: they must be restored to draft before their design can be
/// edited.</para>
/// </remarks>
public sealed class SurveyService(
    IAppDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider time,
    IAuditService audit,
    IValidator<SurveyDefinitionDto> validator,
    ILogger<SurveyService> logger) : ISurveyService
{
    /// <summary>Definition document format written by export and accepted by import.</summary>
    public const int ExportSchemaVersion = 1;

    /// <summary>Generator name written into exported definition documents.</summary>
    public const string ExportGenerator = "SmartSurvey";

    private const string EntityName = "Survey";

    private const string ConcurrencyMessage =
        "This survey was modified by someone else while you were editing it. Reload the survey to get the latest version and apply your changes again.";

    private const string SaveConflictMessage =
        "The survey could not be saved because it conflicts with existing data (for example a link name or an item id that is already in use). Reload the survey and try again.";

    /// <summary>List row projection; translated to SQL with correlated counts per survey.</summary>
    private static readonly Expression<Func<Survey, SurveySummaryDto>> ToSummary = s => new SurveySummaryDto
    {
        Id = s.Id,
        Title = s.Title,
        Description = s.Description,
        Slug = s.Slug,
        Status = s.Status,
        IsTemplate = s.IsTemplate,
        AllowAnonymous = s.AllowAnonymous,
        QuestionCount = s.Questions.Count,
        CompletedResponses = s.Responses.Count(r => r.Status == ResponseStatus.Completed),
        InProgressResponses = s.Responses.Count(r => r.Status == ResponseStatus.InProgress),
        OpensAt = s.OpensAt,
        ClosesAt = s.ClosesAt,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt,
        LastResponseAt = s.Responses.Where(r => r.Status == ResponseStatus.Completed).Max(r => r.SubmittedAt),
        IsPasswordProtected = s.AccessPasswordHash != null,
    };

    /// <summary>How a requested slug that cannot be used is handled for new surveys.</summary>
    private enum SlugConflictPolicy
    {
        /// <summary>Reject a taken slug with a conflict (explicit choice of the designer).</summary>
        Reject,

        /// <summary>Replace a taken or malformed slug with a generated one (copies and import files).</summary>
        Replace,
    }

    /// <inheritdoc />
    public async Task<PagedResult<SurveySummaryDto>> ListAsync(SurveyQuery query, CancellationToken ct = default)
    {
        EnsureAdmin();
        ArgumentNullException.ThrowIfNull(query);

        await using var db = await dbFactory.CreateAsync(ct);
        var surveys = ApplyFilters(db.Surveys.AsNoTracking(), query);

        var total = await surveys.CountAsync(ct);
        var items = await surveys
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .ThenBy(s => s.Title)
            .ThenBy(s => s.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToSummary)
            .ToListAsync(ct);

        return new PagedResult<SurveySummaryDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurveySummaryDto>> ListTemplatesAsync(CancellationToken ct = default)
    {
        EnsureAdmin();

        await using var db = await dbFactory.CreateAsync(ct);
        return await db.Surveys
            .AsNoTracking()
            .Where(s => s.IsTemplate && s.Status != SurveyStatus.Archived)
            .OrderByDescending(s => s.CreatedAt)
            .ThenBy(s => s.Title)
            .Select(ToSummary)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<SurveyDefinitionDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();

        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await WithDesign(db.Surveys.AsNoTracking()).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(EntityName, id);

        return ToDefinition(survey);
    }

    /// <inheritdoc />
    public async Task<SurveyDefinitionDto?> FindBySlugAsync(string slug, CancellationToken ct = default)
    {
        EnsureAdmin();
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var normalized = slug.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await WithDesign(db.Surveys.AsNoTracking()).FirstOrDefaultAsync(s => s.Slug.ToLower() == normalized, ct);

        return survey is null ? null : ToDefinition(survey);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A supplied slug must be valid and unused (<see cref="ConflictException"/> when taken); without
    /// one, a unique slug is generated from the title ("title", "title-2", …).
    /// </remarks>
    public async Task<SurveyDefinitionDto> CreateAsync(SurveyDefinitionDto dto, CancellationToken ct = default)
    {
        EnsureAdmin();
        ArgumentNullException.ThrowIfNull(dto);

        var survey = await AddNewSurveyAsync(SurveyDefinitionCloner.Copy(dto), SlugConflictPolicy.Reject, ct);
        await LogAsync(AuditActions.SurveyCreated, survey.Id, $"Created survey '{survey.Title}' ({DescribeSize(survey)}).", ct);

        return await GetAsync(survey.Id, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Status, publication/closing timestamps and creation metadata are not editable here. An empty
    /// slug keeps the current one so existing share links keep working.
    /// </remarks>
    public async Task<SurveyDefinitionDto> UpdateAsync(Guid id, SurveyDefinitionDto dto, CancellationToken ct = default)
    {
        EnsureAdmin();
        ArgumentNullException.ThrowIfNull(dto);

        var design = SurveyDefinitionCloner.Copy(dto);
        design.Id = id;
        SurveyNormalizer.Normalize(design);

        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await WithDesign(db.Surveys).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(EntityName, id);

        if (survey.Status == SurveyStatus.Archived)
        {
            throw new BusinessRuleException("Archived surveys are read-only. Restore the survey to draft before editing it.");
        }

        if (dto.Version != survey.Version)
        {
            throw new ConflictException(ConcurrencyMessage);
        }

        await ValidateAsync(design, ct);
        survey.AccessPasswordHash = ResolveAccessPasswordHash(design, survey.AccessPasswordHash);
        survey.Slug = await ResolveUpdatedSlugAsync(db, survey, design.Slug, ct);

        SurveyDesignReconciler.Apply(db, survey, design);
        survey.Version++;
        await SaveChangesAsync(db, id, ct);

        await LogAsync(AuditActions.SurveyUpdated, id, $"Updated survey '{survey.Title}' (version {survey.Version}).", ct);
        return await GetAsync(id, ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();

        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(EntityName, id);
        var responseCount = await db.Responses.CountAsync(r => r.SurveyId == id, ct);

        // Only the root is tracked; the database cascades to the design, responses and reports.
        db.Surveys.Remove(survey);
        await SaveChangesAsync(db, id, ct);

        await LogAsync(AuditActions.SurveyDeleted, id, $"Deleted survey '{survey.Title}' with {Count(responseCount, "response")}.", ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Changing to the current status is a no-op. Publishing stamps <c>PublishedAt</c> (and clears
    /// <c>ClosedAt</c> when reopening); closing stamps <c>ClosedAt</c>. Status changes do not bump the
    /// design version, so an open builder session can still save afterwards.
    /// </remarks>
    public async Task<SurveyDefinitionDto> ChangeStatusAsync(Guid id, SurveyStatus status, CancellationToken ct = default)
    {
        EnsureAdmin();
        if (!Enum.IsDefined(status))
        {
            throw new AppValidationException("Status", "Please choose a valid survey status.");
        }

        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(EntityName, id);

        var from = survey.Status;
        if (from == status)
        {
            return await GetAsync(id, ct);
        }

        if (!SurveyStatusTransitions.IsAllowed(from, status))
        {
            throw new BusinessRuleException(SurveyStatusTransitions.DescribeAllowedTargets(from));
        }

        if (status == SurveyStatus.Published)
        {
            await EnsureCanPublishAsync(db, survey, ct);
        }

        ApplyLifecycleTimestamps(survey, status, time.GetUtcNow().UtcDateTime);
        survey.Status = status;
        await SaveChangesAsync(db, id, ct);

        await LogAsync(AuditActions.SurveyStatusChanged, id, $"{from} → {status}", ct);
        return await GetAsync(id, ct);
    }

    /// <inheritdoc />
    /// <remarks>The copy gets new ids for every item, a unique slug and status Draft; responses are not copied.</remarks>
    public async Task<SurveyDefinitionDto> DuplicateAsync(Guid id, DuplicateSurveyRequest request, CancellationToken ct = default)
    {
        EnsureAdmin();
        ArgumentNullException.ThrowIfNull(request);

        var source = await GetAsync(id, ct);
        var copy = SurveyDefinitionCloner.CopyWithNewIds(source);
        copy.Title = BuildCopyTitle(request.Title, source.Title);
        copy.IsTemplate = request.AsTemplate;
        copy.Slug = null;

        // The copy keeps the source's password (same hash); the password itself is never known here.
        string? passwordHash;
        await using (var db = await dbFactory.CreateAsync(ct))
        {
            passwordHash = await db.Surveys.Where(s => s.Id == id).Select(s => s.AccessPasswordHash).FirstOrDefaultAsync(ct);
        }

        var survey = await AddNewSurveyAsync(copy, SlugConflictPolicy.Replace, ct, passwordHash);
        await LogAsync(AuditActions.SurveyDuplicated, survey.Id, $"Created '{survey.Title}' as a copy of '{source.Title}' ({source.Id}).", ct);

        return await GetAsync(survey.Id, ct);
    }

    /// <inheritdoc />
    public async Task<SurveyExportDocument> ExportDefinitionAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();

        var survey = await GetAsync(id, ct);

        // Passwords are not part of a portable definition: set a new one after importing.
        survey.PasswordProtected = false;
        survey.AccessPassword = null;
        var document = new SurveyExportDocument
        {
            SchemaVersion = ExportSchemaVersion,
            Generator = ExportGenerator,
            ExportedAt = time.GetUtcNow().UtcDateTime,
            Survey = survey,
        };

        await LogAsync(AuditActions.SurveyExported, id, $"Exported the definition of '{survey.Title}'.", ct);
        return document;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every id is replaced consistently (references included), so the same file can be imported
    /// any number of times. The survey starts as a Draft; a slug that is malformed or already taken
    /// is replaced by one generated from the title.
    /// </remarks>
    public async Task<SurveyDefinitionDto> ImportDefinitionAsync(SurveyExportDocument document, CancellationToken ct = default)
    {
        EnsureAdmin();
        EnsureImportable(document);

        var design = SurveyDefinitionCloner.CopyWithNewIds(document.Survey);
        if (string.IsNullOrEmpty(design.AccessPassword))
        {
            design.PasswordProtected = false; // a file can't carry a password hash; protect it again after importing
        }

        var survey = await AddNewSurveyAsync(design, SlugConflictPolicy.Replace, ct);
        await LogAsync(AuditActions.SurveyImported, survey.Id, $"Imported survey '{survey.Title}' ({DescribeSize(survey)}).", ct);

        return await GetAsync(survey.Id, ct);
    }

    /// <inheritdoc />
    /// <remarks>The comparison ignores case; a malformed slug is never reported as available.</remarks>
    public async Task<bool> IsSlugAvailableAsync(string slug, Guid? excludeSurveyId = null, CancellationToken ct = default)
    {
        EnsureAdmin();

        var normalized = slug?.Trim().ToLowerInvariant();
        if (normalized is null || !SlugGenerator.IsValid(normalized))
        {
            return false;
        }

        await using var db = await dbFactory.CreateAsync(ct);
        return !await IsSlugTakenAsync(db, normalized, excludeSurveyId, ct);
    }

    /// <inheritdoc />
    public async Task<int> CountAnswersAsync(Guid questionId, CancellationToken ct = default)
    {
        EnsureAdmin();

        await using var db = await dbFactory.CreateAsync(ct);
        return await db.Answers.CountAsync(a => a.QuestionId == questionId, ct);
    }

    /// <summary>Normalises, validates and inserts a new Draft survey built from a private design copy.</summary>
    private async Task<Survey> AddNewSurveyAsync(
        SurveyDefinitionDto design, SlugConflictPolicy slugPolicy, CancellationToken ct, string? existingPasswordHash = null)
    {
        SurveyNormalizer.Normalize(design);
        if (slugPolicy == SlugConflictPolicy.Replace && !SlugGenerator.IsValid(design.Slug))
        {
            design.Slug = null; // regenerate instead of rejecting the whole file
        }

        await ValidateAsync(design, ct);
        var passwordHash = ResolveAccessPasswordHash(design, existingPasswordHash);

        await using var db = await dbFactory.CreateAsync(ct);
        if (await db.Surveys.AnyAsync(s => s.Id == design.Id, ct))
        {
            throw new ConflictException("A survey with the same id already exists. Use Duplicate or Import to create a copy of an existing survey.");
        }

        var slug = await ResolveNewSlugAsync(db, design, slugPolicy, ct);
        var survey = SurveyEntityMapper.ToNewSurvey(design, slug);
        survey.AccessPasswordHash = passwordHash;
        db.Surveys.Add(survey);
        await SaveChangesAsync(db, survey.Id, ct);

        return survey;
    }

    /// <summary>
    /// The access-password hash to store: none when protection is off; a new hash when a password is supplied;
    /// otherwise the current one. Switching protection on without any password is a validation error.
    /// </summary>
    private static string? ResolveAccessPasswordHash(SurveyDefinitionDto design, string? currentHash)
    {
        if (!design.PasswordProtected)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(design.AccessPassword))
        {
            return SurveyPasswordHasher.Hash(design.AccessPassword);
        }

        return currentHash ?? throw new AppValidationException(
            nameof(SurveyDefinitionDto.AccessPassword), "Please enter the password respondents need to open the survey.");
    }

    private async Task ValidateAsync(SurveyDefinitionDto design, CancellationToken ct) =>
        (await validator.ValidateAsync(design, ct)).ThrowIfInvalid();

    /// <summary>Saves and converts concurrency/constraint failures into user-facing conflicts.</summary>
    private async Task SaveChangesAsync(IAppDbContext db, Guid surveyId, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogInformation(ex, "Survey {SurveyId} was modified concurrently.", surveyId);
            throw new ConflictException(ConcurrencyMessage);
        }
        catch (DbUpdateException ex)
        {
            // Slug or id collisions that slipped past the pre-checks (e.g. two admins saving at once).
            logger.LogWarning(ex, "Saving survey {SurveyId} violated a database constraint.", surveyId);
            throw new ConflictException(SaveConflictMessage);
        }
    }

    private Task LogAsync(string action, Guid surveyId, string details, CancellationToken ct) =>
        audit.LogAsync(action, EntityName, surveyId.ToString(), details, ct);

    private void EnsureAdmin()
    {
        if (!currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only administrators can manage surveys.");
        }
    }

    private static IQueryable<Survey> ApplyFilters(IQueryable<Survey> surveys, SurveyQuery query)
    {
        if (query.Status is { } status)
        {
            surveys = surveys.Where(s => s.Status == status);
        }
        else if (!query.IncludeArchived)
        {
            surveys = surveys.Where(s => s.Status != SurveyStatus.Archived);
        }

        if (query.IsTemplate is { } isTemplate)
        {
            surveys = surveys.Where(s => s.IsTemplate == isTemplate);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ToLower on both sides keeps the search case-insensitive on PostgreSQL and SQLite alike.
            var term = query.Search.Trim().ToLowerInvariant();
            surveys = surveys.Where(s =>
                s.Title.ToLower().Contains(term)
                || (s.Description != null && s.Description.ToLower().Contains(term))
                || s.Slug.ToLower().Contains(term));
        }

        return surveys;
    }

    /// <summary>Includes the complete design graph (split queries avoid a cartesian explosion).</summary>
    private static IQueryable<Survey> WithDesign(IQueryable<Survey> surveys) =>
        surveys
            .AsSplitQuery()
            .Include(s => s.Sections)
            .Include(s => s.Questions).ThenInclude(q => q.Options)
            .Include(s => s.LogicRules).ThenInclude(r => r.Conditions);

    /// <summary>Maps to the DTO and lists logic rules in the display order of their targets.</summary>
    private static SurveyDefinitionDto ToDefinition(Survey survey)
    {
        var dto = survey.ToDefinitionDto();

        var positions = new Dictionary<Guid, int>();
        foreach (var section in dto.Sections)
        {
            positions[section.Id] = positions.Count;
            foreach (var question in section.Questions)
            {
                positions[question.Id] = positions.Count;
            }
        }

        dto.LogicRules = dto.LogicRules
            .OrderBy(r => positions.GetValueOrDefault(r.TargetQuestionId ?? r.TargetSectionId ?? Guid.Empty, int.MaxValue))
            .ToList();
        return dto;
    }

    private static async Task<string> ResolveNewSlugAsync(
        IAppDbContext db, SurveyDefinitionDto design, SlugConflictPolicy policy, CancellationToken ct)
    {
        if (design.Slug is null)
        {
            return await GenerateUniqueSlugAsync(db, design.Title, ct);
        }

        if (!await IsSlugTakenAsync(db, design.Slug, null, ct))
        {
            return design.Slug;
        }

        return policy == SlugConflictPolicy.Replace
            ? await GenerateUniqueSlugAsync(db, design.Title, ct)
            : throw SlugTaken(design.Slug);
    }

    /// <summary>An empty slug keeps the current one; a new slug must not be used by another survey.</summary>
    private static async Task<string> ResolveUpdatedSlugAsync(IAppDbContext db, Survey survey, string? requested, CancellationToken ct)
    {
        if (requested is null || string.Equals(requested, survey.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return survey.Slug;
        }

        return await IsSlugTakenAsync(db, requested, survey.Id, ct) ? throw SlugTaken(requested) : requested;
    }

    /// <summary>Slug from the title, made unique with -2, -3, … suffixes.</summary>
    private static async Task<string> GenerateUniqueSlugAsync(IAppDbContext db, string title, CancellationToken ct)
    {
        var baseSlug = SlugGenerator.Generate(title);
        var candidate = baseSlug;
        for (var suffix = 2; await IsSlugTakenAsync(db, candidate, null, ct); suffix++)
        {
            candidate = SlugGenerator.WithSuffix(baseSlug, suffix);
        }

        return candidate;
    }

    private static Task<bool> IsSlugTakenAsync(IAppDbContext db, string slug, Guid? excludeSurveyId, CancellationToken ct)
    {
        var normalized = slug.ToLowerInvariant();
        var matches = db.Surveys.Where(s => s.Slug.ToLower() == normalized);
        if (excludeSurveyId is { } excluded)
        {
            matches = matches.Where(s => s.Id != excluded);
        }

        return matches.AnyAsync(ct);
    }

    private static ConflictException SlugTaken(string slug) =>
        new($"The link name '{slug}' is already used by another survey. Please choose a different one.");

    private static async Task EnsureCanPublishAsync(IAppDbContext db, Survey survey, CancellationToken ct)
    {
        if (survey.IsTemplate)
        {
            throw new BusinessRuleException("Templates cannot be published. Create a survey from this template and publish that survey instead.");
        }

        if (!await db.Questions.AnyAsync(q => q.SurveyId == survey.Id, ct))
        {
            throw new BusinessRuleException("Add at least one question before publishing the survey.");
        }
    }

    private static void ApplyLifecycleTimestamps(Survey survey, SurveyStatus target, DateTime utcNow)
    {
        switch (target)
        {
            case SurveyStatus.Published:
                survey.PublishedAt = utcNow;
                survey.ClosedAt = null; // reopening a closed survey
                break;
            case SurveyStatus.Closed:
                survey.ClosedAt = utcNow;
                break;
        }
    }

    private static void EnsureImportable(SurveyExportDocument? document)
    {
        if (document?.Survey is null)
        {
            throw new AppValidationException("Survey", "The file does not contain a survey definition.");
        }

        if (document.SchemaVersion != ExportSchemaVersion)
        {
            throw new AppValidationException(
                "SchemaVersion",
                $"This file uses definition format version {document.SchemaVersion}, but only version {ExportSchemaVersion} can be imported.");
        }
    }

    private static string BuildCopyTitle(string? requested, string sourceTitle)
    {
        var title = string.IsNullOrWhiteSpace(requested) ? $"Copy of {sourceTitle}" : requested.Trim();
        return title.Length <= SurveyDesignLimits.TitleMaxLength
            ? title
            : title[..SurveyDesignLimits.TitleMaxLength].TrimEnd();
    }

    private static string DescribeSize(Survey survey) =>
        $"{Count(survey.Sections.Count, "page")}, {Count(survey.Questions.Count, "question")}";

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
