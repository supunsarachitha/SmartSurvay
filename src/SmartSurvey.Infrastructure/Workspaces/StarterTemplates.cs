using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Infrastructure.Persistence;
using SmartSurvey.Infrastructure.Persistence.Seed;

namespace SmartSurvey.Infrastructure.Workspaces;

/// <summary>
/// Ready-made survey templates for a new workspace ("create from template" is not empty on day one): customer
/// satisfaction (every question type and display logic), a members-only team pulse check and event feedback — built
/// from the demo designs, with their own system-wide unique links.
/// </summary>
public sealed class StarterTemplates(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time)
{
    /// <summary>Titles of the templates, in creation order.</summary>
    public static readonly IReadOnlyList<string> Titles = ["Customer satisfaction", "Team pulse check", "Event feedback"];

    /// <summary>Adds the starter templates to <paramref name="workspaceId"/>; returns how many were added.</summary>
    /// <param name="workspaceId">The new workspace.</param>
    /// <param name="createdById">Its first admin (recorded as creator), if known.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<int> AddToAsync(Guid workspaceId, Guid? createdById, CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var demo = DemoSurveyFactory.Create(now, createdById);
        var templates = new[] { demo.Customer, demo.Engagement, demo.EventTemplate };

        await using var system = (await dbFactory.CreateDbContextAsync(ct)).UseScope(DataScope.System);
        for (var i = 0; i < templates.Length; i++)
        {
            var template = templates[i];
            template.Title = Titles[i];
            template.Slug = await FreeSlugAsync(system, Titles[i], ct);
            template.IsTemplate = true;
            template.Status = SurveyStatus.Draft;
            template.PublishedAt = null;
            template.ClosedAt = null;
            template.OpensAt = null;
            template.ClosesAt = null;
            template.MaxResponses = null;
            template.CreatedAt = now;
        }

        // Workspace scope: the whole graph (pages, questions, options, logic) is stamped with the workspace.
        await using var db = (await dbFactory.CreateDbContextAsync(ct)).UseScope(DataScope.ForWorkspace(workspaceId));
        db.Surveys.AddRange(templates);
        await db.SaveChangesAsync(ct);
        return templates.Length;
    }

    /// <summary>"customer-satisfaction-template-3fa9c2": links are unique system-wide, so a random suffix is added.</summary>
    private static async Task<string> FreeSlugAsync(AppDbContext system, string title, CancellationToken ct)
    {
        var baseSlug = SlugGenerator.Generate(title) + "-template";
        while (true)
        {
            var candidate = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..6]}";
            if (!await system.Surveys.AnyAsync(s => s.Slug == candidate, ct))
            {
                return candidate;
            }
        }
    }
}
