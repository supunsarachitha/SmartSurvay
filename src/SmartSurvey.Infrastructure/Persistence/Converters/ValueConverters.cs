using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SmartSurvey.Infrastructure.Persistence.Converters;

/// <summary>Marker for JSON column converters (used to apply <c>jsonb</c> on PostgreSQL).</summary>
internal interface IJsonValueConverter;

/// <summary>Shared serializer settings for JSON columns (camelCase, enums as strings).</summary>
internal static class JsonColumn
{
    /// <summary>Serializer options.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes a value.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Deserializes a value, falling back to a new instance for null/empty/invalid JSON.</summary>
    public static T Deserialize<T>(string? json)
        where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch (JsonException)
        {
            // Corrupt / legacy JSON must never make an entity unreadable.
            return new T();
        }
    }
}

/// <summary>Stores a settings object as JSON text (<c>jsonb</c> on PostgreSQL).</summary>
/// <typeparam name="T">Settings type.</typeparam>
internal sealed class JsonValueConverter<T>() : ValueConverter<T, string>(
    v => JsonColumn.Serialize(v),
    v => JsonColumn.Deserialize<T>(v)), IJsonValueConverter
    where T : class, new();

/// <summary>Snapshot/compare JSON settings by value so in-place mutations are detected.</summary>
/// <typeparam name="T">Settings type.</typeparam>
internal sealed class JsonValueComparer<T>() : ValueComparer<T>(
    (a, b) => JsonColumn.Serialize(a) == JsonColumn.Serialize(b),
    v => JsonColumn.Serialize(v).GetHashCode(StringComparison.Ordinal),
    v => JsonColumn.Deserialize<T>(JsonColumn.Serialize(v)))
    where T : class, new();

/// <summary>
/// Ensures every <see cref="DateTime"/> is stored and materialised as UTC. SQLite returns
/// <see cref="DateTimeKind.Unspecified"/> and Npgsql rejects non-UTC values for timestamptz.
/// </summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
