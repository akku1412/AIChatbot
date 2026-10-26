using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>Splits extracted sections into chunks of about <c>targetCharacters</c>, at line boundaries where possible.</summary>
public static class DocumentChunker
{
    public static List<DocumentChunk> Chunk(Guid fileId, IEnumerable<ExtractedSection> sections, int targetCharacters)
    {
        var chunks = new List<DocumentChunk>();
        foreach (var section in sections)
        {
            // Summaries are short and must stay whole.
            var pieces = section.Kind == DocumentChunkKind.Summary
                ? [section.Text]
                : Split(section.Text, targetCharacters);

            for (var i = 0; i < pieces.Count; i++)
            {
                chunks.Add(new DocumentChunk
                {
                    FileId = fileId,
                    ChunkIndex = chunks.Count,
                    Kind = section.Kind,
                    Location = pieces.Count == 1 ? section.Location : $"{section.Location} (part {i + 1})",
                    Content = pieces[i],
                    Source = section.Source,
                });
            }
        }
        return chunks;
    }

    internal static List<string> Split(string text, int target)
    {
        var pieces = new List<string>();
        var remaining = text.AsSpan().Trim();
        while (remaining.Length > target)
        {
            // Prefer a paragraph break, then a line break, then a space, in the last 40% of the window.
            var window = remaining[..target];
            var cut = LastBreak(window, "\n\n", target);
            if (cut < 0) cut = LastBreak(window, "\n", target);
            if (cut < 0) cut = LastBreak(window, " ", target);
            if (cut <= 0) cut = target;

            pieces.Add(remaining[..cut].Trim().ToString());
            remaining = remaining[cut..].TrimStart();
        }
        if (remaining.Length > 0)
        {
            pieces.Add(remaining.ToString());
        }
        return pieces;
    }

    private static int LastBreak(ReadOnlySpan<char> window, string separator, int target)
    {
        var index = window.LastIndexOf(separator.AsSpan());
        return index >= target * 0.6 ? index + separator.Length : -1;
    }
}
