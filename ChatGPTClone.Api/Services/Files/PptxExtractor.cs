using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// PowerPoint (.pptx) via the Open XML SDK (MIT): the text of every slide in presentation order — titles, text boxes,
/// tables — plus its speaker notes, cited as "Slide 4". Macros and embedded objects are never loaded.
/// </summary>
public sealed class PptxExtractor : IDocumentExtractor
{
    public bool CanHandle(string extension) => extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase);

    public ExtractionResult Extract(Stream content, string extension, ExtractionLimits limits)
    {
        PresentationDocument document;
        try
        {
            document = PresentationDocument.Open(content, isEditable: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new DocumentExtractionException("This presentation is damaged or not a valid .pptx file.", "corrupted", ex);
        }

        using (document)
        {
            var presentation = document.PresentationPart;
            var slideIds = presentation?.Presentation?.SlideIdList?.Elements<SlideId>().ToList() ?? [];
            var budget = new TextBudget(limits.MaxCharacters);
            var sections = new List<ExtractedSection>();

            for (var i = 0; i < slideIds.Count && !budget.Exhausted; i++)
            {
                if (slideIds[i].RelationshipId?.Value is not { } relId || presentation!.GetPartById(relId) is not SlidePart slide)
                {
                    continue;
                }

                var text = new StringBuilder();
                foreach (var paragraph in slide.Slide?.Descendants<A.Paragraph>() ?? [])
                {
                    var line = string.Concat(paragraph.Descendants<A.Text>().Select(t => t.Text)).Trim();
                    if (line.Length > 0)
                    {
                        text.AppendLine(line);
                    }
                }

                var notes = slide.NotesSlidePart?.NotesSlide?.Descendants<A.Paragraph>()
                    .Select(p => string.Concat(p.Descendants<A.Text>().Select(t => t.Text)).Trim())
                    .Where(l => l.Length > 0 && !int.TryParse(l, out _)) // the notes page repeats the slide number
                    .ToList() ?? [];
                if (notes.Count > 0)
                {
                    text.AppendLine().AppendLine("Speaker notes:");
                    notes.ForEach(n => text.AppendLine(n));
                }

                var body = text.ToString().Trim();
                if (body.Length > 0)
                {
                    sections.Add(new ExtractedSection($"Slide {i + 1}", budget.Take(body)));
                }
            }

            if (sections.Count == 0)
            {
                throw new DocumentExtractionException("This presentation has no text on its slides.", "empty");
            }
            var warning = budget.Exhausted ? $"Only the first part of this presentation was read (limit {limits.MaxCharacters:N0} characters)." : null;
            return new ExtractionResult(sections, slideIds.Count, warning);
        }
    }
}
