using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Entities;

namespace SmartSurvey.Application.Branding;

/// <summary>
/// Process-wide branding cache (singleton). Holds the current <see cref="BrandingDto"/> and logo,
/// reloads them from the database at most once per <see cref="Ttl"/> (so several app instances converge
/// quickly) and is invalidated immediately by <see cref="BrandingService"/> after changes.
/// </summary>
public sealed class BrandingCache(IServiceScopeFactory scopeFactory, TimeProvider time, IOptions<BrandingOptions> options)
{
    /// <summary>Maximum age of cached branding.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot? _snapshot;

    /// <summary>Current branding (loads on first use / after expiry).</summary>
    public async Task<BrandingDto> GetAsync(CancellationToken ct = default) => (await GetSnapshotAsync(ct)).Dto;

    /// <summary>Current logo, or null.</summary>
    public async Task<BrandingLogo?> GetLogoAsync(CancellationToken ct = default) => (await GetSnapshotAsync(ct)).Logo;

    /// <summary>Drops the cached values so the next read hits the database.</summary>
    public void Invalidate() => _snapshot = null;

    /// <summary>Branding built from configuration only (used when no row exists).</summary>
    public BrandingDto Defaults => new()
    {
        ProductName = string.IsNullOrWhiteSpace(options.Value.ProductName) ? BrandingDefaults.ProductName : options.Value.ProductName.Trim(),
        Tagline = string.IsNullOrWhiteSpace(options.Value.Tagline) ? null : options.Value.Tagline.Trim(),
        IconName = BrandingService.IsValidIconName(options.Value.IconName) ? options.Value.IconName : BrandingDefaults.IconName,
    };

    private async Task<Snapshot> GetSnapshotAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var current = _snapshot;
        if (current is not null && current.LoadedAt + Ttl > now)
        {
            return current;
        }

        await _gate.WaitAsync(ct);
        try
        {
            current = _snapshot;
            if (current is not null && current.LoadedAt + Ttl > now)
            {
                return current;
            }

            current = await LoadAsync(now, ct);
            _snapshot = current;
            return current;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Snapshot> LoadAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IAppDbContextFactory>();
            await using var db = await factory.CreateAsync(ct);
            var row = await db.BrandingSettings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == BrandingSettings.SingletonId, ct);
            if (row is null)
            {
                return new Snapshot(Defaults, null, now);
            }

            var dto = BrandingService.ToDto(row);
            var logo = row.LogoContent is { Length: > 0 } && row.LogoContentType is not null
                ? new BrandingLogo(row.LogoContent, row.LogoContentType, row.Version)
                : null;
            return new Snapshot(dto, logo, now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never break page rendering because branding cannot be loaded (e.g. database not
            // migrated yet): fall back to the defaults and retry after a short delay.
            return new Snapshot(Defaults, null, now - Ttl + TimeSpan.FromSeconds(5));
        }
    }

    private sealed record Snapshot(BrandingDto Dto, BrandingLogo? Logo, DateTimeOffset LoadedAt);
}

/// <summary>Branding use cases (reads via <see cref="BrandingCache"/>; admin-only, audited writes).</summary>
public sealed partial class BrandingService(
    BrandingCache cache,
    IAppDbContextFactory dbFactory,
    ICurrentUser currentUser,
    IAuditService audit) : IBrandingService
{
    /// <inheritdoc />
    public Task<BrandingDto> GetAsync(CancellationToken ct = default) => cache.GetAsync(ct);

    /// <inheritdoc />
    public Task<BrandingLogo?> GetLogoAsync(CancellationToken ct = default) => cache.GetLogoAsync(ct);

    /// <inheritdoc />
    public async Task<BrandingDto> UpdateAsync(UpdateBrandingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureSuperAdmin();

        var errors = new Dictionary<string, string[]>();
        var name = request.ProductName?.Trim() ?? string.Empty;
        var tagline = string.IsNullOrWhiteSpace(request.Tagline) ? null : request.Tagline.Trim();
        var icon = request.IconName?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            errors[nameof(request.ProductName)] = ["Please enter a product name."];
        }
        else if (name.Length > BrandingDefaults.MaxProductNameLength)
        {
            errors[nameof(request.ProductName)] = [$"The product name must be at most {BrandingDefaults.MaxProductNameLength} characters."];
        }

        if (tagline?.Length > BrandingDefaults.MaxTaglineLength)
        {
            errors[nameof(request.Tagline)] = [$"The tagline must be at most {BrandingDefaults.MaxTaglineLength} characters."];
        }

        if (!IsValidIconName(icon))
        {
            errors[nameof(request.IconName)] = ["Choose a valid Bootstrap icon (for example \"bi-ui-checks\")."];
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }

        return await MutateAsync(row =>
        {
            row.ProductName = name;
            row.Tagline = tagline;
            row.IconName = icon;
        }, $"Name \"{name}\", icon {icon}", ct);
    }

    /// <inheritdoc />
    public async Task<BrandingDto> SetLogoAsync(byte[] content, string? fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureSuperAdmin();

        var contentType = LogoImageInspector.DetectContentType(content, out var error)
            ?? throw new AppValidationException("Logo", error ?? "Unsupported image.");

        return await MutateAsync(row =>
        {
            row.LogoContent = content;
            row.LogoContentType = contentType;
        }, $"Logo uploaded ({contentType}, {content.Length / 1024.0:0.#} KB{(string.IsNullOrWhiteSpace(fileName) ? string.Empty : ", " + Path.GetFileName(fileName))})", ct);
    }

    /// <inheritdoc />
    public async Task<BrandingDto> RemoveLogoAsync(CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        return await MutateAsync(row =>
        {
            row.LogoContent = null;
            row.LogoContentType = null;
        }, "Logo removed", ct);
    }

    /// <inheritdoc />
    public async Task<BrandingDto> ResetAsync(CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        var defaults = cache.Defaults;
        return await MutateAsync(row =>
        {
            row.ProductName = defaults.ProductName;
            row.Tagline = defaults.Tagline;
            row.IconName = defaults.IconName;
            row.LogoContent = null;
            row.LogoContentType = null;
        }, "Branding reset to defaults", ct);
    }

    /// <summary>True for Bootstrap icon classes such as <c>bi-ui-checks</c>.</summary>
    public static bool IsValidIconName(string? iconName) =>
        !string.IsNullOrWhiteSpace(iconName) && iconName.Length <= 60 && IconRegex().IsMatch(iconName);

    internal static BrandingDto ToDto(BrandingSettings row) => new()
    {
        ProductName = row.ProductName,
        Tagline = row.Tagline,
        IconName = row.IconName,
        HasLogo = row.LogoContent is { Length: > 0 },
        LogoContentType = row.LogoContentType,
        Version = row.Version,
        UpdatedAt = row.UpdatedAt ?? row.CreatedAt,
    };

    private async Task<BrandingDto> MutateAsync(Action<BrandingSettings> change, string details, CancellationToken ct)
    {
        await using (var db = await dbFactory.CreateAsync(ct))
        {
            var row = await db.BrandingSettings.FirstOrDefaultAsync(b => b.Id == BrandingSettings.SingletonId, ct);
            if (row is null)
            {
                var defaults = cache.Defaults;
                row = new BrandingSettings
                {
                    ProductName = defaults.ProductName,
                    Tagline = defaults.Tagline,
                    IconName = defaults.IconName,
                };
                db.BrandingSettings.Add(row);
            }

            change(row);
            row.Version++;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                throw new ConflictException("Branding was changed by someone else at the same time. Please reload and try again.");
            }
        }

        cache.Invalidate();
        await audit.LogAsync(AuditActions.BrandingUpdated, "Branding", BrandingSettings.SingletonId.ToString(), details, ct);
        return await cache.GetAsync(ct);
    }

    /// <summary>Branding is system-wide, so only super admins change it (workspace admins cannot).</summary>
    private void EnsureSuperAdmin()
    {
        if (!currentUser.IsSuperAdmin)
        {
            throw new ForbiddenException("Only super admins can change the branding.");
        }
    }

    [GeneratedRegex("^bi-[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IconRegex();
}
