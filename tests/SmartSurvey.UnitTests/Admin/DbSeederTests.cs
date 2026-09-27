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
