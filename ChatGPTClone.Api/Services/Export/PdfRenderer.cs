using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ChatGPTClone.Api.Services.Export;

/// <summary>
/// A4 PDF via QuestPDF (Community License: free for individuals and businesses under USD 1M revenue).
/// Headings, paragraphs with bold/italic/code, lists, quotes, code blocks and real tables; page numbers in the footer.
/// Fonts are the bundled Noto families (SIL OFL, in /Fonts), so output is identical on Windows and Linux servers and
/// Hindi (Devanagari) text is shaped correctly instead of showing boxes.
/// </summary>
public static class PdfRenderer
{
    private static readonly string[] BodyFonts = ["Noto Sans", "Noto Sans Devanagari"];
    private static readonly string[] CodeFonts = ["Noto Sans Mono", "Noto Sans Devanagari"];
    private const string Accent = "#4F46E5";

    static PdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Register the shipped fonts explicitly (not relying on directory scanning or on fonts installed on the server).
        var fontsFolder = Path.Combine(AppContext.BaseDirectory, "Fonts");
        foreach (var file in Directory.Exists(fontsFolder) ? Directory.GetFiles(fontsFolder, "*.ttf") : [])
        {
            using var font = File.OpenRead(file);
            FontManager.RegisterFont(font);
        }
    }

    public static byte[] Render(IReadOnlyList<DocBlock> blocks, ExportContext context) =>
        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(style => style.FontSize(11).LineHeight(1.4f).FontFamily(BodyFonts).FontColor(Colors.Grey.Darken4));

                page.Header().PaddingBottom(10).BorderBottom(0.75f).BorderColor(Colors.Grey.Lighten2).PaddingBottom(4).Row(row =>
                {
                    row.RelativeItem().Text(context.Title).FontSize(9).SemiBold().FontColor(Accent);
                    row.AutoItem().Text($"AIChat · {context.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC").FontSize(9).FontColor(Colors.Grey.Medium);
                });

                page.Content().Column(column =>
                {
                    column.Spacing(7);
                    foreach (var block in blocks)
                    {
                        AddBlock(column, block);
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(9).FontColor(Colors.Grey.Medium));
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = context.Title,
            Author = "AIChat",
            Creator = "AIChat",
            Producer = "AIChat",
            CreationDate = new DateTimeOffset(DateTime.SpecifyKind(context.CreatedAtUtc, DateTimeKind.Utc)),
        })
        .GeneratePdf();

    private static void AddBlock(ColumnDescriptor column, DocBlock block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var size = heading.Level switch { 1 => 20f, 2 => 16f, 3 => 13.5f, _ => 12f };
                column.Item().PaddingTop(heading.Level <= 2 ? 8 : 4).Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(size).Bold().FontColor(Colors.Grey.Darken4));
                    AddRuns(text, heading.Runs);
                });
                break;

            case ParagraphBlock paragraph:
                column.Item().Text(text => AddRuns(text, paragraph.Runs));
                break;

            case ListItemBlock item:
                column.Item().PaddingLeft(16 * item.Depth).Row(row =>
                {
                    row.ConstantItem(item.Marker.Length > 2 ? 24 : 16).Text(item.Marker);
                    row.RelativeItem().Text(text => AddRuns(text, item.Runs));
                });
                break;

            case CodeBlock code:
                column.Item().Background(Colors.Grey.Lighten4).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(8)
                    .Text(code.Text).FontFamily(CodeFonts).FontSize(9.5f).LineHeight(1.3f);
                break;

            case QuoteBlock quote:
                column.Item().BorderLeft(3).BorderColor(Colors.Indigo.Lighten3).PaddingLeft(10).Text(text =>
                {
                    text.DefaultTextStyle(style => style.Italic().FontColor(Colors.Grey.Darken2));
                    AddRuns(text, quote.Runs);
                });
                break;

            case TableBlock table:
                column.Item().PaddingVertical(4).Table(grid =>
                {
                    grid.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in table.Header)
                        {
                            columns.RelativeColumn();
                        }
                    });
                    grid.Header(header =>
                    {
                        foreach (var cell in table.Header)
                        {
                            header.Cell().Background(Colors.Indigo.Lighten5).Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                                .Padding(5).Text(cell).SemiBold().FontSize(10);
                        }
                    });
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row)
                        {
                            grid.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(5).Text(cell).FontSize(10);
                        }
                    }
                });
                break;

            case RuleBlock:
                column.Item().PaddingVertical(4).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten2);
                break;
        }
    }

    private static void AddRuns(TextDescriptor text, IReadOnlyList<TextRun> runs)
    {
        foreach (var run in runs)
        {
            var span = text.Span(run.Text);
            if (run.Bold)
            {
                span.Bold();
            }
            if (run.Italic)
            {
                span.Italic();
            }
            if (run.Code)
            {
                span.FontFamily(CodeFonts).FontSize(10).BackgroundColor(Colors.Grey.Lighten4);
            }
        }
    }
}
