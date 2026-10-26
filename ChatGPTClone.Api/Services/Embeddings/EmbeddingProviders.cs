using System.ClientModel;
using System.Runtime.InteropServices;
using ChatGPTClone.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using OpenAI.Embeddings;

namespace ChatGPTClone.Api.Services.Embeddings;

/// <summary>Questions and passages are embedded differently by some models (e5 uses "query: " / "passage: ").</summary>
public enum EmbeddingKind
{
    Query,
    Passage,
}

/// <summary>Turns text into L2-normalised vectors for semantic search.</summary>
public interface IEmbeddingProvider
{
    /// <summary>False when not configured (no model files / no key): retrieval then uses keywords only.</summary>
    bool IsAvailable { get; }

    /// <summary>Stored with every vector; vectors from different models are never compared.</summary>
    string Model { get; }

    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingKind kind, CancellationToken cancellationToken);
}

public static class Vectors
{
    /// <summary>Cosine similarity of two L2-normalised vectors (= dot product).</summary>
    public static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var length = Math.Min(a.Length, b.Length);
        float sum = 0;
        for (var i = 0; i < length; i++)
        {
            sum += a[i] * b[i];
        }
        return sum;
    }

    public static byte[] ToBytes(float[] vector) => MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

    public static float[] FromBytes(byte[] bytes) => MemoryMarshal.Cast<byte, float>(bytes).ToArray();

    public static float[] Normalize(float[] vector)
    {
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }
        return vector;
    }
}

/// <summary>
/// multilingual-e5-small (MIT, 384 dimensions, 100 languages incl. Hindi) running locally with ONNX Runtime (int8
/// model, ~118 MB). Text is tokenised with the model's SentencePiece vocabulary (XLM-RoBERTa id mapping), embedded,
/// mean-pooled over real tokens and L2-normalised. Free, offline, no data leaves the server.
/// </summary>
public sealed class LocalE5EmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private const int MaxTokens = 512;

    private readonly InferenceSession? _session;
    private readonly SentencePieceTokenizer? _tokenizer;
    private readonly bool _needsTokenTypes;
    private readonly SemaphoreSlim _gate = new(Math.Max(1, Environment.ProcessorCount / 2));

    public LocalE5EmbeddingProvider(IOptions<EmbeddingOptions> options, IHostEnvironment environment, ILogger<LocalE5EmbeddingProvider> logger)
    {
        Model = options.Value.Model;
        var modelPath = Resolve(options.Value.LocalModelPath, environment);
        var tokenizerPath = Resolve(options.Value.LocalTokenizerPath, environment);
        if (modelPath is null || tokenizerPath is null)
        {
            logger.LogWarning("Semantic search is disabled: embedding model files not found ({Model}, {Tokenizer}). " +
                "Run scripts/download-models.ps1. Document retrieval falls back to keywords.",
                options.Value.LocalModelPath, options.Value.LocalTokenizerPath);
            return;
        }

        try
        {
            using (var spm = File.OpenRead(tokenizerPath))
            {
                _tokenizer = SentencePieceTokenizer.Create(spm, false, false);
            }
            var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2) };
            _session = new InferenceSession(modelPath, sessionOptions);
            _needsTokenTypes = _session.InputMetadata.ContainsKey("token_type_ids");
        }
        catch (Exception ex)
        {
            // E.g. a damaged download or not enough memory: keep the app working with keyword search.
            logger.LogError(ex, "Semantic search is disabled: the embedding model could not be loaded. Document retrieval falls back to keywords.");
            _session?.Dispose();
            _session = null;
            _tokenizer = null;
        }
    }

    public bool IsAvailable => _session is not null;

    public string Model { get; }

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingKind kind, CancellationToken cancellationToken)
    {
        if (_session is null || _tokenizer is null)
        {
            throw new InvalidOperationException("The local embedding model is not available.");
        }

        var prefix = kind == EmbeddingKind.Query ? "query: " : "passage: ";
        var results = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                // CPU-bound inference: off the request thread.
                var text = prefix + texts[i];
                results[i] = await Task.Run(() => EmbedOne(text), cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }
        return results;
    }

    private float[] EmbedOne(string text)
    {
        // XLM-RoBERTa ids: <s>=0, </s>=2, <unk>=3, SentencePiece pieces shifted by one.
        var pieces = _tokenizer!.EncodeToIds(text, false, false);
        var ids = new List<long>(Math.Min(pieces.Count, MaxTokens - 2) + 2) { 0 };
        ids.AddRange(pieces.Take(MaxTokens - 2).Select(id => id == 0 ? 3L : id + 1L));
        ids.Add(2);
        var length = ids.Count;

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids.ToArray(), [1, length])),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(Enumerable.Repeat(1L, length).ToArray(), [1, length])),
        };
        if (_needsTokenTypes)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[length], [1, length])));
        }

        using var output = _session!.Run(inputs);
        var hidden = output.First().AsTensor<float>();
        var dimensions = hidden.Dimensions[2];
        var vector = new float[dimensions];
        for (var t = 0; t < length; t++)
        {
            for (var d = 0; d < dimensions; d++)
            {
                vector[d] += hidden[0, t, d];
            }
        }
        return Vectors.Normalize(vector); // mean pooling + L2 normalisation (the mean's 1/n cancels out)
    }

    private static string? Resolve(string path, IHostEnvironment environment)
    {
        if (Path.IsPathRooted(path))
        {
            return File.Exists(path) ? path : null;
        }
        return new[] { Path.Combine(environment.ContentRootPath, path), Path.Combine(AppContext.BaseDirectory, path) }
            .FirstOrDefault(File.Exists);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _gate.Dispose();
    }
}

/// <summary>OpenAI embeddings (text-embedding-3-small by default). Needs Ai:OpenAI:ApiKey. Not used with Groq-only setups.</summary>
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingClient? _client;

    public OpenAiEmbeddingProvider(IOptions<EmbeddingOptions> options, IOptions<AiOptions> ai)
    {
        Model = options.Value.OpenAIModel;
        var key = ai.Value.OpenAI.ApiKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            _client = new EmbeddingClient(Model, new ApiKeyCredential(key));
        }
    }

    public bool IsAvailable => _client is not null;

    public string Model { get; }

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingKind kind, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("OpenAI embeddings are not configured.");
        }
        var result = await _client.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken);
        return result.Value.Select(e => Vectors.Normalize(e.ToFloats().ToArray())).ToArray();
    }
}

/// <summary>Semantic search switched off: keyword retrieval only.</summary>
public sealed class NoEmbeddingProvider : IEmbeddingProvider
{
    public bool IsAvailable => false;
    public string Model => "none";
    public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingKind kind, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Embeddings are disabled.");
}
