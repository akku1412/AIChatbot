using System.Text;
using System.Text.RegularExpressions;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services.Embeddings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services.Files;

public interface IDocumentContextBuilder
{
    /// <summary>
    /// Builds the &lt;documents&gt; block for the AI: computed summaries always, then either all text (small files)
    /// or the chunks most relevant to the question (large files), within the configured character budget.
    /// Null when there are no files.
    /// </summary>
    Task<DocumentContext?> BuildAsync(IReadOnlyList<UploadedFile> files, string question, CancellationToken cancellationToken);
}

/// <summary>
/// The prompt text plus the passages it contains (ChunkId → relevance score). The AI cites a passage as
/// [file:ChunkId]; only ids in <see cref="Passages"/> are accepted as sources.
/// </summary>
public sealed record DocumentContext(string Text, IReadOnlyDictionary<long, double> Passages);

/// <summary>
/// Hybrid retrieval over the stored chunks: keyword scoring (term frequency × inverse document frequency) blended with
/// semantic similarity from the embedding model (Embeddings:VectorWeight), so "how did sales change?" also finds a
/// passage about "revenue grew 12%". Without embeddings (not configured, not indexed yet, model failure) it is
/// keyword-only, as before. Each file gets a fair share of the budget so comparisons across files work. When the
/// question has no distinctive words ("summarize this"), chunks are sampled evenly across each file.
/// </summary>
public sealed partial class DocumentContextBuilder(
    AppDbContext db,
    IEmbeddingProvider embeddings,
    IOptions<FileUploadOptions> options,
    IOptions<EmbeddingOptions> embeddingOptions,
    ILogger<DocumentContextBuilder> logger) : IDocumentContextBuilder
{
    /// <summary>The citation marker the AI writes, e.g. [file:123].</summary>
    public const string CitationPrefix = "file:";

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "of", "to", "in", "on", "for", "with", "is", "are", "was", "were", "be", "this",
        "that", "these", "those", "it", "its", "what", "which", "who", "how", "why", "when", "where", "do", "does", "did",
        "can", "could", "please", "me", "my", "i", "you", "your", "from", "by", "about", "as", "at", "file", "files",
        "document", "documents", "pdf", "word", "excel", "sheet", "spreadsheet", "uploaded", "attached", "tell", "give",
        "explain", "summarize", "summarise", "summary", "show", "find", "list", "all", "any", "using", "only", "there",
        "kya", "hai", "ka", "ki", "ke", "ko", "me", "mein", "batao", "is", "ye", "yeh",
    };

    private readonly FileUploadOptions _options = options.Value;
    private readonly double _vectorWeight = Math.Clamp(embeddingOptions.Value.VectorWeight, 0, 1);

    public async Task<DocumentContext?> BuildAsync(IReadOnlyList<UploadedFile> files, string question, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return null;
        }

        var fileIds = files.Select(f => f.FileId).ToList();
        var chunks = await db.DocumentChunks.AsNoTracking()
            .Where(c => fileIds.Contains(c.FileId))
            .OrderBy(c => c.FileId).ThenBy(c => c.ChunkIndex)
            .ToListAsync(cancellationToken);
        var byFile = chunks.GroupBy(c => c.FileId).ToDictionary(g => g.Key, g => g.ToList());

        var budget = _options.MaxDocumentContextCharacters;
        var terms = Terms(question);
        var textChunks = chunks.Where(c => c.Kind == DocumentChunkKind.Text).ToList();
        var scores = terms.Count == 0
            ? new Dictionary<long, double>() // general question: even spread, no ranking
            : await HybridScoresAsync(textChunks, question, terms, cancellationToken);
        double ScoreOf(DocumentChunk c) => scores.GetValueOrDefault(c.ChunkId);

        // Pass 1: summaries (always), then each file's share of text.
        var selected = new Dictionary<Guid, List<DocumentChunk>>();
        var used = 0;
        foreach (var file in files)
        {
            var list = byFile.GetValueOrDefault(file.FileId) ?? [];
            var summaries = list.Where(c => c.Kind == DocumentChunkKind.Summary).ToList();
            selected[file.FileId] = TakeWithin(summaries, Math.Max(budget / 4 / files.Count, 1500), ref used);
        }

        var share = Math.Max(0, (budget - used) / files.Count);
        var leftovers = new List<DocumentChunk>();
        foreach (var file in files)
        {
            var text = (byFile.GetValueOrDefault(file.FileId) ?? []).Where(c => c.Kind == DocumentChunkKind.Text).ToList();
            var spent = 0;
            foreach (var chunk in Rank(text, ScoreOf))
            {
                if (spent + chunk.Content.Length <= share)
                {
                    selected[file.FileId].Add(chunk);
                    spent += chunk.Content.Length;
                }
                else
                {
                    leftovers.Add(chunk);
                }
            }
            used += spent;
        }

        // Pass 2: give unused budget to the best remaining chunks of any file.
        foreach (var chunk in leftovers.OrderByDescending(ScoreOf))
        {
            if (used + chunk.Content.Length <= budget)
            {
                selected[chunk.FileId].Add(chunk);
                used += chunk.Content.Length;
            }
        }

        var passages = selected.Values.SelectMany(l => l).ToDictionary(c => c.ChunkId, ScoreOf);
        return new DocumentContext(Format(files, byFile, selected), passages);
    }

    /// <summary>
    /// Keyword and semantic scores, each scaled to 0..1 over these chunks, blended by VectorWeight. Chunks without a
    /// vector from the current model get the keyword score only.
    /// </summary>
    private async Task<Dictionary<long, double>> HybridScoresAsync(List<DocumentChunk> chunks, string question,
        IReadOnlyList<string> terms, CancellationToken cancellationToken)
    {
        var idf = InverseDocumentFrequency(chunks, terms);
        var keyword = chunks.ToDictionary(c => c.ChunkId, c => KeywordScore(c, terms, idf));
        var maxKeyword = keyword.Values.DefaultIfEmpty(0).Max();
        var result = keyword.ToDictionary(k => k.Key, k => maxKeyword > 0 ? k.Value / maxKeyword : 0);

        var similarity = await SimilaritiesAsync(chunks, question, cancellationToken);
        if (similarity.Count == 0)
        {
            return result;
        }

        // Min-max scaling: e5 cosines sit in a narrow band (≈0.7–0.9), so raw values would barely move the ranking.
        var min = similarity.Values.Min();
        var range = similarity.Values.Max() - min;
        foreach (var (chunkId, cosine) in similarity)
        {
            var semantic = range > 1e-6 ? (cosine - min) / range : 0;
            result[chunkId] = _vectorWeight * semantic + (1 - _vectorWeight) * result[chunkId];
        }
        return result;
    }

    /// <summary>Cosine similarity of the question to each indexed chunk; empty when semantic search isn't available.</summary>
    private async Task<Dictionary<long, double>> SimilaritiesAsync(List<DocumentChunk> chunks, string question,
        CancellationToken cancellationToken)
    {
        if (!embeddings.IsAvailable || chunks.Count == 0)
        {
            return [];
        }

        var ids = chunks.Select(c => c.ChunkId).ToList();
        var model = embeddings.Model;
        var stored = await db.ChunkEmbeddings.AsNoTracking()
            .Where(e => ids.Contains(e.ChunkId) && e.Model == model)
            .Select(e => new { e.ChunkId, e.Vector })
            .ToListAsync(cancellationToken);
        if (stored.Count == 0)
        {
            return [];
        }

        float[] query;
        try
        {
            query = (await embeddings.EmbedAsync([question], EmbeddingKind.Query, cancellationToken))[0];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Embedding the question failed; using keyword search only");
            return [];
        }

        var result = new Dictionary<long, double>();
        foreach (var row in stored)
        {
            var vector = Vectors.FromBytes(row.Vector);
            if (vector.Length == query.Length)
            {
                result[row.ChunkId] = Vectors.Cosine(query, vector);
            }
        }
        return result;
    }

    private static List<DocumentChunk> TakeWithin(List<DocumentChunk> chunks, int limit, ref int used)
    {
        var taken = new List<DocumentChunk>();
        var spent = 0;
        foreach (var chunk in chunks)
        {
            if (spent + chunk.Content.Length > limit && taken.Count > 0)
            {
                break;
            }
            taken.Add(chunk);
            spent += chunk.Content.Length;
        }
        used += spent;
        return taken;
    }

    /// <summary>Most relevant first; with no scores at all, an even spread starting at the beginning.</summary>
    private static List<DocumentChunk> Rank(List<DocumentChunk> chunks, Func<DocumentChunk, double> score)
    {
        if (chunks.Count == 0)
        {
            return chunks;
        }

        if (chunks.All(c => score(c) == 0))
        {
            // Beginning first (usually the overview), then evenly spaced chunks across the document.
            var order = new List<DocumentChunk> { chunks[0] };
            for (var stride = Math.Max(1, chunks.Count / 2); stride >= 1 && order.Count < chunks.Count; stride /= 2)
            {
                for (var i = 0; i < chunks.Count; i += stride)
                {
                    if (!order.Contains(chunks[i]))
                    {
                        order.Add(chunks[i]);
                    }
                }
                if (stride == 1)
                {
                    break;
                }
            }
            return order;
        }

        return chunks
            .OrderByDescending(score)
            .ThenBy(c => c.ChunkIndex)
            .ToList();
    }

    private static double KeywordScore(DocumentChunk chunk, IReadOnlyList<string> terms, Dictionary<string, double> idf)
    {
        if (terms.Count == 0)
        {
            return 0;
        }
        var text = chunk.Content.ToLowerInvariant() + " " + chunk.Location.ToLowerInvariant();
        double score = 0;
        foreach (var term in terms)
        {
            var count = CountOccurrences(text, term);
            if (count > 0)
            {
                score += (1 + Math.Log(count)) * idf.GetValueOrDefault(term, 1);
            }
        }
        return score;
    }

    private static Dictionary<string, double> InverseDocumentFrequency(List<DocumentChunk> chunks, IReadOnlyList<string> terms)
    {
        var result = new Dictionary<string, double>();
        foreach (var term in terms)
        {
            var df = chunks.Count(c => c.Content.Contains(term, StringComparison.OrdinalIgnoreCase));
            result[term] = Math.Log(1 + (chunks.Count + 1.0) / (df + 1.0));
        }
        return result;
    }

    internal static IReadOnlyList<string> Terms(string question) =>
        WordPattern().Matches(question.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => (w.Length >= 3 || w.Any(char.IsDigit)) && !StopWords.Contains(w))
            .Distinct()
            .ToList();

    private static int CountOccurrences(string text, string term)
    {
        var count = 0;
        for (var i = text.IndexOf(term, StringComparison.Ordinal); i >= 0; i = text.IndexOf(term, i + term.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static string Format(IReadOnlyList<UploadedFile> files, Dictionary<Guid, List<DocumentChunk>> all,
        Dictionary<Guid, List<DocumentChunk>> selected)
    {
        var output = new StringBuilder("<documents>\n");
        foreach (var file in files)
        {
            var chosen = selected[file.FileId].OrderBy(c => c.ChunkIndex).ToList();
            var total = all.GetValueOrDefault(file.FileId)?.Count ?? 0;
            var coverage = chosen.Count >= total ? "complete" : $"excerpts ({chosen.Count} of {total} parts)";
            var kind = FileSignature.Supported.TryGetValue(file.FileExtension, out var f) ? f.Description : "document";
            var pages = file.PageCount is { } p ? (file.FileExtension == ".pdf" ? $" pages=\"{p}\"" : $" sheets=\"{p}\"") : string.Empty;

            output.Append($"<document name=\"{EscapeAttribute(file.OriginalFileName)}\" type=\"{kind}\"{pages} coverage=\"{coverage}\">\n");
            if (file.StatusMessage is { Length: > 0 } note)
            {
                output.Append($"[Note: {Escape(note)}]\n");
            }
            foreach (var chunk in chosen)
            {
                var label = chunk.Kind == DocumentChunkKind.Summary ? $"Computed summary - {chunk.Location}" : chunk.Location;
                if (chunk.Source == ChunkSource.Ocr)
                {
                    label += " (read by OCR; may contain recognition errors)";
                }
                output.Append($"<passage id=\"{CitationPrefix}{chunk.ChunkId}\" location=\"{EscapeAttribute(label)}\">\n")
                    .Append(Escape(chunk.Content))
                    .Append("\n</passage>\n");
            }
            output.Append("</document>\n");
        }
        return output.Append("</documents>").ToString();
    }

    /// <summary>Stops document text from closing or faking our delimiter tags (&lt;documents&gt;, &lt;document&gt;, &lt;passage&gt;).</summary>
    internal static string Escape(string text) =>
        DelimiterTag().Replace(text, m => m.Value.Insert(1, "\\"));

    private static string EscapeAttribute(string text) => Escape(text).Replace("\"", "'");

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}\-_.]*")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"</?\s*(documents?|passage)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DelimiterTag();
}
