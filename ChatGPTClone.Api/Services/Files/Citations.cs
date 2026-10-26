using System.Text.RegularExpressions;
using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// The AI cites document passages as [file:123] (also [file:12, file:13]). Only ids that were actually given to the
/// AI for that reply are kept as sources, so an invented or foreign id never links to anything.
/// </summary>
public static partial class Citations
{
    /// <summary>Cited chunk ids in order of first appearance.</summary>
    public static IReadOnlyList<long> Ids(string text) =>
        Marker().Matches(text)
            .SelectMany(m => IdPattern().Matches(m.Value))
            .Select(m => long.TryParse(m.Groups[1].Value, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

    /// <summary>
    /// Replaces each marker with <paramref name="label"/>(ids); a null label (unknown id) drops that id, and a marker
    /// with no known ids is removed. Used by exports, which turn markers into "(report.pdf, Page 3)".
    /// </summary>
    public static string Replace(string text, Func<long, string?> label) =>
        Marker().Replace(text, m =>
        {
            var labels = IdPattern().Matches(m.Value)
                .Select(i => long.TryParse(i.Groups[1].Value, out var id) ? label(id) : null)
                .Where(l => l is not null)
                .Distinct()
                .ToList();
            return labels.Count == 0 ? string.Empty : $"({string.Join("; ", labels)})";
        });

    /// <summary>The message text with its markers as "(report.pdf, Page 3)" (needs Sources → Chunk → File loaded).</summary>
    public static string Readable(Message message) =>
        message.Sources.Count == 0 && !message.Content.Contains("file:", StringComparison.OrdinalIgnoreCase)
            ? message.Content
            : Replace(message.Content, id => message.Sources.FirstOrDefault(s => s.ChunkId == id)?.Chunk is { File: not null } chunk
                ? $"{chunk.File.OriginalFileName}, {chunk.Location}"
                : null);

    [GeneratedRegex(@"\[\s*file:\d+(?:\s*[,;]\s*(?:file:)?\d+)*\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex Marker();

    [GeneratedRegex(@"(?:file:)?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex IdPattern();
}
