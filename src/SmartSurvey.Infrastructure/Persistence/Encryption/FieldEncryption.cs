using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SmartSurvey.Infrastructure.Persistence.Encryption;

/// <summary>"Encryption" configuration section.</summary>
public sealed class FieldEncryptionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Encryption";

    /// <summary>
    /// Encrypt respondents' free-text answers, "Other" texts and browser details in the database (default: true).
    /// Switching it off only affects new writes; existing encrypted values stay readable.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Encrypts and decrypts individual database values.</summary>
public interface IFieldProtector
{
    /// <summary>Encrypts a value for storage.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts a stored value. Values written before encryption was enabled (no prefix) are returned unchanged;
    /// a value whose key is missing becomes a placeholder text instead of failing the whole page.
    /// </summary>
    string Unprotect(string stored);
}

/// <summary>
/// <see cref="IFieldProtector"/> using ASP.NET Core Data Protection (AES-256-CBC + HMAC-SHA256, automatic key rotation).
/// Stored values look like <c>enc:v1:…</c>. The keys live outside the database — in the key ring folder
/// (<c>DataProtection:KeysPath</c>, a Docker volume) — so a copy of the database alone doesn't reveal the answers.
/// Back the key ring up together with the database: without it, encrypted values cannot be read.
/// </summary>
public sealed class DataProtectionFieldProtector(IDataProtectionProvider provider, ILogger<DataProtectionFieldProtector> logger) : IFieldProtector
{
    /// <summary>Marker of encrypted values (also used by the one-time migration of older plaintext values).</summary>
    public const string Prefix = "enc:v1:";

    /// <summary>Shown instead of a value that cannot be decrypted (missing or foreign key ring).</summary>
    public const string UnreadablePlaceholder = "[encrypted answer — the key to read it is not available]";

    private readonly IDataProtector _protector = provider.CreateProtector("SmartSurvey.FieldEncryption.v1");
    private int _failures;

    /// <inheritdoc />
    public string Protect(string plaintext) => Prefix + _protector.Protect(plaintext);

    /// <inheritdoc />
    public string Unprotect(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return stored; // written before encryption was enabled
        }

        try
        {
            return _protector.Unprotect(stored[Prefix.Length..]);
        }
        catch (CryptographicException ex)
        {
            if (Interlocked.Increment(ref _failures) <= 5)
            {
                logger.LogError(ex, "An encrypted database value could not be decrypted. Is the data-protection key ring (DataProtection:KeysPath) missing or from another installation?");
            }

            return UnreadablePlaceholder;
        }
    }
}

/// <summary>EF Core value converter that stores strings through an <see cref="IFieldProtector"/>.</summary>
internal sealed class EncryptedStringConverter(IFieldProtector protector)
    : ValueConverter<string, string>(value => protector.Protect(value), stored => protector.Unprotect(stored));

/// <summary>
/// Options extension carrying the <see cref="IFieldProtector"/> to <see cref="AppDbContext.OnModelCreating"/>.
/// Without it (tests, design-time tools) columns are stored as plain text.
/// </summary>
internal sealed class FieldEncryptionExtension(IFieldProtector protector) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>The protector used by the value converters.</summary>
    public IFieldProtector Protector { get; } = protector;

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
    {
    }

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "FieldEncryption ";

        // The protector is not an EF service: every context can share one internal service provider.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["SmartSurvey:FieldEncryption"] = "1";
    }
}

/// <summary>Builds one EF model per protector (the converters capture it), plus the default plain-text model.</summary>
internal sealed class EncryptionAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), context.GetService<IDbContextOptions>().FindExtension<FieldEncryptionExtension>()?.Protector, designTime);
}

/// <summary>Registration helpers.</summary>
public static class FieldEncryptionExtensions
{
    /// <summary>Encrypts the sensitive answer columns of <see cref="AppDbContext"/> with <paramref name="protector"/>.</summary>
    public static DbContextOptionsBuilder UseFieldEncryption(this DbContextOptionsBuilder builder, IFieldProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new FieldEncryptionExtension(protector));
        builder.ReplaceService<IModelCacheKeyFactory, EncryptionAwareModelCacheKeyFactory>();
        return builder;
    }
}
