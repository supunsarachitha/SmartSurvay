using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.Identity;
using SmartSurvey.Infrastructure.Persistence.Seed;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Admin;

public sealed class DbSeederTests
{
    [Fact]
    public async Task Creates_roles_and_the_configured_admin_once()
    {
        await using var host = await IdentityTestHost.CreateAsync();

        await SeedAsync(host);
        await SeedAsync(host);

        await using var db = await host.CreateDbContextAsync();
        Assert.Equal(AppRoles.All.Count, await db.Roles.CountAsync());
        var admin = await db.Users.SingleAsync();
        Assert.Equal("admin@test.local", admin.Email);
        Assert.Equal(DbSeeder.AdminDisplayName, admin.DisplayName);
        Assert.True(await host.WithAsync<UserManager<ApplicationUser>, bool>(users => users.IsInRoleAsync(admin, AppRoles.Admin)));
        Assert.True(await host.WithAsync<UserManager<ApplicationUser>, bool>(users => users.CheckPasswordAsync(admin, "Admin123!")));
    }

    [Fact]
    public async Task Without_a_password_no_admin_is_created()
    {
        await using var host = await IdentityTestHost.CreateAsync(new Dictionary<string, string?> { ["Seed:AdminPassword"] = "" });

        await SeedAsync(host);

        await using var db = await host.CreateDbContextAsync();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Equal(AppRoles.All.Count, await db.Roles.CountAsync());
    }

    [Fact]
    public async Task An_existing_regular_account_with_the_admin_email_is_never_promoted()
    {
        await using var host = await IdentityTestHost.CreateAsync();
        var squatter = await host.AddUserAsync(Guid.NewGuid(), "admin@test.local", AppRoles.User);

        await SeedAsync(host);

        Assert.False(await host.WithAsync<UserManager<ApplicationUser>, bool>(async users =>
            await users.IsInRoleAsync((await users.FindByIdAsync(squatter.Id.ToString()))!, AppRoles.Admin)));
    }

    [Fact]
    public async Task Demo_data_is_created_once_with_surveys_responses_and_a_report()
    {
        await using var host = await IdentityTestHost.CreateAsync(new Dictionary<string, string?> { ["Seed:DemoData"] = "true" });

        await SeedAsync(host);
        var first = await CountsAsync(host);
        await SeedAsync(host);
        var second = await CountsAsync(host);

        Assert.True(first.Surveys >= 3, $"expected several demo surveys, got {first.Surveys}");
        Assert.True(first.Completed > 50, $"expected many completed responses, got {first.Completed}");
        Assert.True(first.Reports >= 1);
        Assert.Equal(first, second); // idempotent
        await using var db = await host.CreateDbContextAsync();
        Assert.True(await db.Users.AnyAsync(u => u.Email == "user@smartsurvey.local"));
        Assert.True(await db.Surveys.AnyAsync(s => s.Status == SurveyStatus.Published));
    }

    [Fact]
    public async Task The_admin_is_created_in_the_configured_workspace()
    {
        await using var host = await IdentityTestHost.CreateAsync(new Dictionary<string, string?>
        {
            ["Seed:WorkspaceName"] = "Head office",
            ["Seed:WorkspaceSlug"] = "head-office",
        });

        await SeedAsync(host);

        await using var db = await host.CreateDbContextAsync();
        var workspace = await db.Workspaces.SingleAsync(w => w.Slug == "head-office");
        Assert.Equal("Head office", workspace.Name);
        Assert.Equal(workspace.Id, (await db.Users.SingleAsync(u => u.Email == "admin@test.local")).WorkspaceId);
    }

    [Fact]
    public async Task A_super_admin_without_a_workspace_is_created_once_when_configured()
    {
        await using var host = await IdentityTestHost.CreateAsync(new Dictionary<string, string?> { ["Seed:SuperAdminPassword"] = "Super123!" });

        await SeedAsync(host);
        await SeedAsync(host);

        await using var db = await host.CreateDbContextAsync();
        var super = await db.Users.SingleAsync(u => u.Email == "superadmin@smartsurvey.local");
        Assert.Null(super.WorkspaceId);
        Assert.Equal(DbSeeder.SuperAdminDisplayName, super.DisplayName);
        Assert.Equal([AppRoles.SuperAdmin], await host.WithAsync<UserManager<ApplicationUser>, IList<string>>(users => users.GetRolesAsync(super)));
        Assert.True(await host.WithAsync<UserManager<ApplicationUser>, bool>(users => users.CheckPasswordAsync(super, "Super123!")));
    }

    [Fact]
    public async Task An_existing_account_with_the_super_admin_email_is_never_promoted()
    {
        await using var host = await IdentityTestHost.CreateAsync(new Dictionary<string, string?> { ["Seed:SuperAdminPassword"] = "Super123!" });
        var squatter = await host.AddUserAsync(Guid.NewGuid(), "superadmin@smartsurvey.local", AppRoles.User);

        await SeedAsync(host);

        Assert.Empty(await host.WithAsync<UserManager<ApplicationUser>, IList<ApplicationUser>>(users => users.GetUsersInRoleAsync(AppRoles.SuperAdmin)));
        Assert.Equal(TestWorkspaces.DefaultId, (await host.WithAsync<UserManager<ApplicationUser>, ApplicationUser?>(users => users.FindByIdAsync(squatter.Id.ToString())))!.WorkspaceId);
    }

    [Fact]
    public async Task Demo_data_keeps_the_two_demo_workspaces_apart()
    {
        await using var host = await IdentityTestHost.CreateAsync(new Dictionary<string, string?> { ["Seed:DemoData"] = "true" });

        await SeedAsync(host);
        await SeedAsync(host);

        await using var db = await host.CreateDbContextAsync();
        var main = await db.Workspaces.SingleAsync(w => w.Slug == "default");
        var acme = await db.Workspaces.SingleAsync(w => w.Slug == DbSeeder.DemoSecondWorkspaceSlug);
        var acmeAdmin = await db.Users.SingleAsync(u => u.Email == "admin@acme.local");

        Assert.Equal(acme.Id, acmeAdmin.WorkspaceId);
        Assert.Equal(["acme-team-offsite-feedback"], await db.Surveys.Where(s => s.WorkspaceId == acme.Id).Select(s => s.Slug).ToListAsync());
        Assert.Empty(await db.Responses.Where(r => r.WorkspaceId == acme.Id).ToListAsync());
        Assert.True(await db.Surveys.CountAsync(s => s.WorkspaceId == main.Id) >= 3);
        Assert.All(await db.Users.Where(u => u.Email!.EndsWith("@smartsurvey.local") || u.Email!.EndsWith("@example.com")).ToListAsync(),
            u => Assert.Equal(main.Id, u.WorkspaceId));
    }

    private static Task SeedAsync(IdentityTestHost host) => host.WithAsync<DbSeeder>(seeder => seeder.SeedAsync());

    private static async Task<(int Surveys, int Completed, int Reports, int Users)> CountsAsync(IdentityTestHost host)
    {
        await using var db = await host.CreateDbContextAsync();
        return (
            await db.Surveys.CountAsync(),
            await db.Responses.CountAsync(r => r.Status == ResponseStatus.Completed),
            await db.Reports.CountAsync(),
            await db.Users.CountAsync());
    }
}
