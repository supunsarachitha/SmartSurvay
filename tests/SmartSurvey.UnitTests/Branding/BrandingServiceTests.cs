using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Common;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Branding;

public sealed class BrandingServiceTests : IAsyncLifetime
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3];

    private readonly SqliteTestDatabase _db = new();
    private readonly RecordingAuditService _audit = new();
    private ServiceProvider _provider = null!;
    private BrandingCache _cache = null!;
    private BrandingService _service = null!;

    public Task InitializeAsync()
    {
        // BrandingCache resolves IAppDbContextFactory from a fresh scope (it is a singleton).
        _provider = new ServiceCollection().AddSingleton<IAppDbContextFactory>(_db).BuildServiceProvider();
        _cache = new BrandingCache(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _db.Time,
            Options.Create(new BrandingOptions { ProductName = "Acme Surveys", Tagline = "Ask better", IconName = "bi-star" }));
        _db.CurrentUser.ActAsSuperAdmin(); // branding is system-wide: super admins manage it
        _service = new BrandingService(_cache, _db, _db.CurrentUser, _audit);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Defaults_come_from_configuration_until_customised()
    {
        var brand = await _service.GetAsync();

        Assert.Equal("Acme Surveys", brand.ProductName);
        Assert.Equal("Ask better", brand.Tagline);
        Assert.Equal("bi-star", brand.IconName);
        Assert.False(brand.HasLogo);
        Assert.Null(brand.LogoUrl);
        Assert.StartsWith("branding/favicon?v=", brand.FaviconUrl);
    }

    [Fact]
    public async Task Update_persists_trims_and_is_visible_immediately()
    {
        var updated = await _service.UpdateAsync(new UpdateBrandingRequest { ProductName = "  Pulse  ", Tagline = " ", IconName = "bi-heart-pulse" });

        Assert.Equal("Pulse", updated.ProductName);
        Assert.Null(updated.Tagline);
        Assert.Equal("bi-heart-pulse", updated.IconName);
        Assert.Equal(1, updated.Version);
        Assert.Equal("Pulse", (await _service.GetAsync()).ProductName);
        Assert.Contains(_audit.Entries, e => e.Action == AuditActions.BrandingUpdated);
    }

    [Theory]
    [InlineData("", "bi-star", "ProductName")]
    [InlineData("Ok", "star", "IconName")]
    [InlineData("Ok", "bi-<script>", "IconName")]
    [InlineData("Ok", "bi-star\" onmouseover=\"x", "IconName")]
    public async Task Update_validates_input(string name, string icon, string errorKey)
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.UpdateAsync(new UpdateBrandingRequest { ProductName = name, IconName = icon }));

        Assert.Contains(errorKey, ex.Errors.Keys);
    }

    [Fact]
    public async Task Update_rejects_too_long_name()
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.UpdateAsync(new UpdateBrandingRequest { ProductName = new string('x', 81), IconName = "bi-star" }));
    }


    [Fact]
    public async Task Workspace_admins_cannot_change_the_system_wide_branding()
    {
        _db.CurrentUser.ActAsAdmin();

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.UpdateAsync(new UpdateBrandingRequest { ProductName = "X", IconName = "bi-star" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SetLogoAsync(Png, "logo.png"));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.RemoveLogoAsync());
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.ResetAsync());
    }
    [Fact]
    public async Task Non_admins_cannot_change_branding()
    {
        _db.CurrentUser.ActAsRespondent();

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.UpdateAsync(new UpdateBrandingRequest { ProductName = "X", IconName = "bi-star" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SetLogoAsync(Png, "logo.png"));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.RemoveLogoAsync());
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.ResetAsync());
    }

    [Fact]
    public async Task Logo_upload_detects_type_from_signature_and_bumps_version()
    {
        var brand = await _service.SetLogoAsync(Png, "logo.jpg"); // extension is ignored

        Assert.True(brand.HasLogo);
        Assert.Equal("image/png", brand.LogoContentType);
        Assert.Equal($"branding/logo?v={brand.Version}", brand.LogoUrl);

        var logo = await _service.GetLogoAsync();
        Assert.NotNull(logo);
        Assert.Equal(Png, logo.Content);
    }

    [Fact]
    public async Task Remove_and_reset_clear_the_logo()
    {
        await _service.UpdateAsync(new UpdateBrandingRequest { ProductName = "Custom", IconName = "bi-gem" });
        await _service.SetLogoAsync(Png, null);

        var removed = await _service.RemoveLogoAsync();
        Assert.False(removed.HasLogo);
        Assert.Equal("Custom", removed.ProductName);

        await _service.SetLogoAsync(Png, null);
        var reset = await _service.ResetAsync();
        Assert.False(reset.HasLogo);
        Assert.Equal("Acme Surveys", reset.ProductName);
        Assert.Equal("bi-star", reset.IconName);
    }

    [Fact]
    public async Task Cache_is_refreshed_after_its_time_to_live()
    {
        await _service.GetAsync(); // prime cache with defaults
        await using (var ctx = _db.CreateContext())
        {
            ctx.BrandingSettings.Add(new Domain.Entities.BrandingSettings { ProductName = "Changed elsewhere", IconName = "bi-gem" });
            await ctx.SaveChangesAsync();
        }

        Assert.Equal("Acme Surveys", (await _service.GetAsync()).ProductName); // still cached
        _db.Time.Advance(BrandingCache.Ttl + TimeSpan.FromSeconds(1));
        Assert.Equal("Changed elsewhere", (await _service.GetAsync()).ProductName);
    }
}

public class LogoImageInspectorTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1 }, "image/jpeg")]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' }, "image/gif")]
    [InlineData(new byte[] { 0, 0, 1, 0, 1, 0 }, "image/x-icon")]
    public void Detects_binary_formats(byte[] content, string expected) =>
        Assert.Equal(expected, LogoImageInspector.DetectContentType(content, out _));

    [Fact]
    public void Detects_webp()
    {
        var webp = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");
        Assert.Equal("image/webp", LogoImageInspector.DetectContentType(webp, out _));
    }

    [Fact]
    public void Accepts_plain_svg()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><defs><linearGradient id=\"g\"/></defs><circle cx=\"5\" cy=\"5\" r=\"4\" fill=\"url(#g)\"/><use href=\"#g\"/></svg>");
        Assert.Equal("image/svg+xml", LogoImageInspector.DetectContentType(svg, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("<svg><script>alert(1)</script></svg>")]
    [InlineData("<svg onload=\"alert(1)\"></svg>")]
    [InlineData("<svg><a href=\"javascript:alert(1)\"><text>x</text></a></svg>")]
    [InlineData("<svg><foreignObject><div>x</div></foreignObject></svg>")]
    [InlineData("<svg><image href=\"https://evil.example/x.png\"/></svg>")]
    public void Rejects_svg_with_active_content(string svg)
    {
        Assert.Null(LogoImageInspector.DetectContentType(Encoding.UTF8.GetBytes(svg), out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_unknown_empty_and_oversized_files()
    {
        Assert.Null(LogoImageInspector.DetectContentType(Encoding.ASCII.GetBytes("MZ\x90 not an image"), out _));
        Assert.Null(LogoImageInspector.DetectContentType([], out _));
        var big = new byte[BrandingDefaults.MaxLogoBytes + 1];
        big[0] = 0xFF; big[1] = 0xD8; big[2] = 0xFF;
        Assert.Null(LogoImageInspector.DetectContentType(big, out var error));
        Assert.Contains("KB", error);
    }
}
