using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds roles, the initial administrator and (optionally) demo data. Runs at every start-up and is
/// idempotent: each step checks what already exists and only adds what is missing.
/// </summary>
/// <remarks>
/// Steps:
/// <list type="number">
/// <item>Every role of <see cref="AppRoles.All"/> exists.</item>
/// <item>When no user is a workspace admin and <see cref="SeedOptions.CreateAdmin"/> is set, the workspace
/// <see cref="SeedOptions.WorkspaceSlug"/> is ensured and the account <see cref="SeedOptions.AdminEmail"/>
/// is created in it with <see cref="SeedOptions.AdminPassword"/>.</item>
/// <item>With <see cref="SeedOptions.DemoData"/>: in the (oldest) admin's workspace the demo user is ensured
/// and — only while that workspace contains no surveys — demo surveys, demo respondent accounts,
/// responses and a sample report are inserted in one transaction.</item>
/// </list>
/// </remarks>
public sealed class DbSeeder(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<SeedOptions> options,
    TimeProvider time,
    ILogger<DbSeeder> logger)
{
    /// <summary>Display name of the seeded administrator.</summary>
    public const string AdminDisplayName = "Administrator";

    /// <summary>Display name of the demo respondent.</summary>
    public const string DemoUserDisplayName = "Demo User";

    /// <summary>Seed of the random generator used for the demo responses (deterministic demo data).</summary>
    public const int DemoRandomSeed = 42;

    /// <summary>Runs all seeding steps (idempotent).</summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var settings = options.Value;

        await EnsureRolesAsync();
        var admin = await EnsureAdminAsync(settings, ct);

        if (!settings.DemoData)
        {
            return;
        }

        var workspaceId = admin?.WorkspaceId ?? await EnsureWorkspaceAsync(settings, ct);
        var demoUserId = await EnsureDemoUserAsync(settings, workspaceId);
        await SeedDemoContentAsync(settings, workspaceId, admin?.Id, demoUserId, ct);
    }

    /// <summary>Returns the id of the seed workspace (<see cref="SeedOptions.WorkspaceSlug"/>), creating it when missing.</summary>
    private async Task<Guid> EnsureWorkspaceAsync(SeedOptions settings, CancellationToken ct)
    {
        var slug = string.IsNullOrWhiteSpace(settings.WorkspaceSlug) ? "default" : settings.WorkspaceSlug.Trim().ToLowerInvariant();
        await using var db = (await dbFactory.CreateDbContextAsync(ct)).UseScope(DataScope.System);
        if (await db.Workspaces.Where(w => w.Slug == slug).Select(w => (Guid?)w.Id).FirstOrDefaultAsync(ct) is { } existing)
        {
            return existing;
        }

        var workspace = new Workspace
        {
            Name = string.IsNullOrWhiteSpace(settings.WorkspaceName) ? "Default workspace" : settings.WorkspaceName.Trim(),
            Slug = slug,
        };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created workspace {Slug}", slug);
        return workspace.Id;
    }

    /// <summary>Creates missing roles. Failing here is fatal: authorization depends on the roles.</summary>
    private async Task EnsureRolesAsync()
    {
        foreach (var role in AppRoles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Could not create role '{role}': {Describe(result)}");
            }

            logger.LogInformation("Created role {Role}", role);
        }
    }

    /// <summary>
    /// Returns the (oldest) workspace admin and their workspace, creating the initial admin (and the seed
    /// workspace) when needed and allowed.
    /// </summary>
    private async Task<(Guid Id, Guid WorkspaceId)?> EnsureAdminAsync(SeedOptions settings, CancellationToken ct)
    {
        var admins = (await userManager.GetUsersInRoleAsync(AppRoles.Admin)).Where(a => a.WorkspaceId.HasValue).ToList();
        if (admins.Count > 0)
        {
            var oldest = admins.OrderBy(a => a.CreatedAt).First();
            return (oldest.Id, oldest.WorkspaceId!.Value);
        }

        if (!settings.CreateAdmin)
        {
            logger.LogWarning("No administrator account exists and Seed:CreateAdmin is disabled");
            return null;
        }

        var email = settings.AdminEmail?.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(settings.AdminPassword))
        {
            logger.LogWarning(
                "No administrator account exists. Set Seed:AdminEmail and Seed:AdminPassword (e.g. via environment variables or user secrets) to create one at start-up");
            return null;
        }

        // Never promote an existing account: anyone could have registered that address (e-mail
        // confirmation is optional), so promoting it would hand out administrator rights.
        if (await FindByEmailOrNameAsync(email) is not null)
        {
            logger.LogWarning(
                "No administrator exists, but the account {Email} already exists as a regular user; it is not promoted automatically. Grant the Admin role manually or configure another Seed:AdminEmail",
                email);
            return null;
        }

        var workspaceId = await EnsureWorkspaceAsync(settings, ct);
        var admin = NewUser(email, AdminDisplayName, workspaceId);
        if (!await CreateUserAsync(admin, settings.AdminPassword, [AppRoles.Admin, AppRoles.User]))
        {
            return null;
        }

        logger.LogInformation("Created administrator account {Email}", email);
        return (admin.Id, workspaceId);
    }

    /// <summary>Returns the id of the demo respondent (a member of the demo workspace), creating it when missing.</summary>
    private async Task<Guid?> EnsureDemoUserAsync(SeedOptions settings, Guid workspaceId)
    {
        var email = settings.DemoUserEmail?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning("Seed:DemoUserEmail is empty; the demo user is not created");
            return null;
        }

        if (await FindByEmailOrNameAsync(email) is { } existing)
        {
            if (existing.WorkspaceId == workspaceId)
            {
                return existing.Id;
            }

            logger.LogWarning("The demo user {Email} belongs to another workspace; demo responses are not linked to it", email);
            return null;
        }

        if (string.IsNullOrWhiteSpace(settings.DemoUserPassword))
        {
            logger.LogWarning("Seed:DemoUserPassword is empty; the demo user {Email} is not created", email);
            return null;
        }

        var user = NewUser(email, DemoUserDisplayName, workspaceId);
        if (!await CreateUserAsync(user, settings.DemoUserPassword, [AppRoles.User]))
        {
            return null;
        }

        logger.LogInformation("Created demo user {Email}", email);
        return user.Id;
    }

    /// <summary>
    /// Inserts the demo content when the database has no surveys yet. Everything is written in one
    /// transaction, so an interrupted run leaves no half-seeded database behind (which the "no surveys"
    /// check would otherwise never repair).
    /// </summary>
    private async Task SeedDemoContentAsync(SeedOptions settings, Guid workspaceId, Guid? adminId, Guid? demoUserId, CancellationToken ct)
    {
        // Workspace scope: every survey, response and report is stamped with the demo workspace.
        await using var db = (await dbFactory.CreateDbContextAsync(ct)).UseScope(DataScope.ForWorkspace(workspaceId));
        if (await db.Surveys.AnyAsync(ct))
        {
            logger.LogInformation("Demo content skipped: the workspace already contains surveys");
            return;
        }

        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var employeeIds = await EnsureDemoEmployeesAsync(db, workspaceId, now, ct);

        var surveys = DemoSurveyFactory.Create(now, adminId);
        db.Surveys.AddRange(surveys.All);
        await db.SaveChangesAsync(ct);

        var accounts = new DemoRespondentAccounts(demoUserId, settings.DemoUserEmail?.Trim(), employeeIds);
        var generated = new DemoResponseGenerator(new Random(DemoRandomSeed), now).Generate(surveys, accounts);
        db.Responses.AddRange(generated.Responses);
        await db.SaveChangesAsync(ct);

        var report = DemoReportFactory.CustomerOverview(surveys.Customer, now, adminId);
        db.Reports.Add(report);
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        if (generated.Discarded > 0)
        {
            logger.LogWarning("{Count} generated demo responses failed validation and were skipped", generated.Discarded);
        }

        logger.LogInformation(
            "Seeded demo content: {Surveys} surveys, {Responses} responses ({Completed} completed), {Employees} demo respondent accounts and the report '{Report}'",
            surveys.All.Count,
            generated.Responses.Count,
            generated.Responses.Count(r => r.SubmittedAt.HasValue),
            employeeIds.Count,
            report.Name);
    }

    /// <summary>
    /// Ensures the demo employee accounts exist (respondents of the login-required pulse survey) and
    /// returns their ids. They are inserted in bulk on the demo context (inside its transaction) and
    /// have no password, so nobody can sign in with them. Addresses already used in another workspace are
    /// skipped (e-mail addresses are unique system-wide).
    /// </summary>
    private async Task<IReadOnlyList<Guid>> EnsureDemoEmployeesAsync(AppDbContext db, Guid workspaceId, DateTime now, CancellationToken ct)
    {
        var people = DemoPeople.Employees();
        var normalized = people.Select(p => userManager.NormalizeEmail(p.Email)!).ToList();
        var existing = await db.Users
            .Where(u => u.NormalizedEmail != null && normalized.Contains(u.NormalizedEmail))
            .Select(u => new { u.Id, u.NormalizedEmail, u.WorkspaceId })
            .ToDictionaryAsync(u => u.NormalizedEmail!, u => (u.Id, u.WorkspaceId), ct);

        var userRoleName = roleManager.NormalizeKey(AppRoles.User);
        var userRoleId = await db.Roles.Where(r => r.NormalizedName == userRoleName).Select(r => r.Id).FirstAsync(ct);

        var ids = new List<Guid>(people.Count);
        for (var i = 0; i < people.Count; i++)
        {
            if (existing.TryGetValue(normalized[i], out var found))
            {
                if (found.WorkspaceId == workspaceId)
                {
                    ids.Add(found.Id);
                }

                continue;
            }

            var employee = NewUser(people[i].Email, people[i].DisplayName, workspaceId);
            employee.NormalizedEmail = normalized[i];
            employee.NormalizedUserName = userManager.NormalizeName(people[i].Email);
            employee.CreatedAt = now.AddDays(-120 + i); // joined before the pulse survey was published

            db.Users.Add(employee);
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = employee.Id, RoleId = userRoleId });
            ids.Add(employee.Id);
        }

        await db.SaveChangesAsync(ct);
        return ids;
    }

    private ApplicationUser NewUser(string email, string displayName, Guid? workspaceId) => new()
    {
        WorkspaceId = workspaceId,
        UserName = email,
        Email = email,
        EmailConfirmed = true,
        DisplayName = displayName,
        CreatedAt = time.GetUtcNow().UtcDateTime,
        LockoutEnabled = true,
    };

    /// <summary>Creates a user with a password and roles; logs and returns false on failure.</summary>
    private async Task<bool> CreateUserAsync(ApplicationUser user, string password, IEnumerable<string> roles)
    {
        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            result = await userManager.AddToRolesAsync(user, roles);
            if (!result.Succeeded)
            {
                await userManager.DeleteAsync(user); // do not leave an account without its roles behind
            }
        }

        if (!result.Succeeded)
        {
            logger.LogError("Could not create the account {Email}: {Errors}", user.Email, Describe(result));
        }

        return result.Succeeded;
    }

    private async Task<ApplicationUser?> FindByEmailOrNameAsync(string email) =>
        await userManager.FindByEmailAsync(email) ?? await userManager.FindByNameAsync(email);

    private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));
}
