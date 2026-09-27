using SmartSurvey.Application.Reports;

namespace SmartSurvey.Application.Exports;

/// <summary>Supported export formats.</summary>
public enum ExportFormat
{
    /// <summary>PDF document (QuestPDF) with charts.</summary>
    Pdf = 0,

    /// <summary>Comma-separated values (RFC 4180, UTF-8 with BOM for Excel).</summary>
    Csv = 1,

    /// <summary>Plain text with ASCII tables.</summary>
    Txt = 2,

    /// <summary>Excel workbook (one worksheet per widget / raw data).</summary>
    Xlsx = 3,

    /// <summary>JSON document.</summary>
    Json = 4,
}

/// <summary>A generated file ready to be downloaded.</summary>
/// <param name="Content">File bytes.</param>
/// <param name="ContentType">MIME type.</param>
/// <param name="FileName">Suggested file name including extension.</param>
public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>Format metadata helpers.</summary>
public static class ExportFormatExtensions
{
    /// <summary>MIME type of the format.</summary>
    public static string ContentType(this ExportFormat format) => format switch
    {
        ExportFormat.Pdf => "application/pdf",
        ExportFormat.Csv => "text/csv",
        ExportFormat.Txt => "text/plain",
        ExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ExportFormat.Json => "application/json",
        _ => "application/octet-stream",
    };

    /// <summary>File extension without dot.</summary>
    public static string FileExtension(this ExportFormat format) => format switch
    {
        ExportFormat.Pdf => "pdf",
        ExportFormat.Csv => "csv",
        ExportFormat.Txt => "txt",
        ExportFormat.Xlsx => "xlsx",
        ExportFormat.Json => "json",
        _ => "bin",
    };

    /// <summary>Parses "pdf", "CSV", … (case-insensitive). Returns false for unknown values.</summary>
    public static bool TryParse(string? value, out ExportFormat format) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out format) && Enum.IsDefined(format);

    /// <summary>Builds a safe file name such as <c>customer-satisfaction-report-20260926-1530.pdf</c>.</summary>
    public static string BuildFileName(string baseName, ExportFormat format, DateTime utcNow)
    {
        var slug = Common.SlugGenerator.Generate(baseName);
        return $"{slug}-{utcNow:yyyyMMdd-HHmm}.{format.FileExtension()}";
    }
}

/// <summary>
/// Serialises a <see cref="ReportResult"/> into one file format. Implementations are registered in
/// DI; add a new format by implementing this interface and registering it — the report service,
/// UI and API discover exporters through <c>IEnumerable&lt;IReportExporter&gt;</c>.
/// </summary>
public interface IReportExporter
{
    /// <summary>Format produced by this exporter.</summary>
    ExportFormat Format { get; }

    /// <summary>Serialises the report.</summary>
    Task<byte[]> ExportAsync(ReportResult report, CancellationToken ct = default);
}

/// <summary>Exports raw response data (one row per response, one column per question).</summary>
public interface IResponseExportService
{
    /// <summary>Formats supported for raw exports (CSV, XLSX, JSON).</summary>
    IReadOnlyList<ExportFormat> SupportedFormats { get; }

    /// <summary>
    /// Exports all responses of a survey (completed only unless <paramref name="includeInProgress"/>).
    /// Throws <see cref="Common.BusinessRuleException"/> for unsupported formats.
    /// </summary>
    Task<ExportFile> ExportResponsesAsync(Guid surveyId, ExportFormat format, bool includeInProgress = false, CancellationToken ct = default);
}
