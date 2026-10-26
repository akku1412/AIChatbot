namespace ChatGPTClone.Api.Services.Export;

public enum ExportFormat
{
    Txt,
    Pdf,
    Docx,
    Xlsx,
}

public static class ExportFormats
{
    public static bool TryParse(string? value, out ExportFormat format) =>
        Enum.TryParse(value?.Trim().TrimStart('.'), ignoreCase: true, out format) && Enum.IsDefined(format);

    public static string Extension(this ExportFormat format) => format switch
    {
        ExportFormat.Txt => ".txt",
        ExportFormat.Pdf => ".pdf",
        ExportFormat.Docx => ".docx",
        _ => ".xlsx",
    };

    public static string ContentType(this ExportFormat format) => format switch
    {
        ExportFormat.Txt => "text/plain; charset=utf-8",
        ExportFormat.Pdf => "application/pdf",
        ExportFormat.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };
}

/// <summary>
/// What a renderer needs besides the content: used for document titles and properties. Visible times are written in
/// <paramref name="TimeZone"/> (UTC when not given); document metadata stays UTC.
/// </summary>
public sealed record ExportContext(string Title, DateTime CreatedAtUtc, UserTimeZone? TimeZone = null)
{
    public UserTimeZone Zone => TimeZone ?? UserTimeZone.Utc;

    /// <summary>"2026-10-26 17:14 UTC+05:30" — the header line's time.</summary>
    public string CreatedAtText => Zone.Format(CreatedAtUtc);
}

/// <summary>The content can't be produced in this format (e.g. XLSX from a reply without any table).</summary>
public sealed class ExportNotPossibleException(string message) : Exception(message);
