using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;

namespace SmartSurvey.Infrastructure.Exports;

/// <summary>
/// Exports a report as an indented JSON document (camelCase, enums as strings) wrapped in a small
/// envelope that identifies the format, so other tools can consume computed reports.
/// </summary>
public sealed class JsonReportExporter : IReportExporter
{
    /// <summary>Value of the envelope's <c>format</c> property.</summary>
    public const string FormatName = "smartsurvey.report";

    /// <summary>Version of the envelope layout.</summary>
    public const int FormatVersion = 1;

    /// <summary>Serializer settings shared by the JSON exports.</summary>
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Keep non-Latin answers readable; HTML-sensitive characters are still escaped.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() },
    };

    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Json;

    /// <inheritdoc />
    public Task<byte[]> ExportAsync(ReportResult report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        var document = new
        {
            format = FormatName,
            version = FormatVersion,
            generator = report.ProductName,
            report,
        };
        return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(document, Options));
    }
}
