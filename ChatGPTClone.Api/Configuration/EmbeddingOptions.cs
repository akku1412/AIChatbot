using System.ComponentModel.DataAnnotations;

namespace ChatGPTClone.Api.Configuration;

/// <summary>
/// Bound from "Embeddings" (separate from the chat provider: Groq has no embedding model).
/// Provider: "Local" (multilingual-e5-small ONNX, free, offline), "OpenAI" (text-embedding-3-small, needs a key) or "None"
/// (keyword retrieval only).
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    [Required]
    public string Provider { get; init; } = "Local";

    /// <summary>Stored with every vector; vectors of different models are never compared.</summary>
    [Required]
    public string Model { get; init; } = "multilingual-e5-small";

    /// <summary>Local provider: ONNX model and SentencePiece tokenizer (scripts/download-models.ps1 fetches both).</summary>
    public string LocalModelPath { get; init; } = "AiModels/multilingual-e5-small/model_quantized.onnx";
    public string LocalTokenizerPath { get; init; } = "AiModels/multilingual-e5-small/sentencepiece.bpe.model";

    /// <summary>OpenAI provider: the model name (the key is Ai:OpenAI:ApiKey).</summary>
    public string OpenAIModel { get; init; } = "text-embedding-3-small";

    /// <summary>Weight of the semantic score in hybrid ranking; the rest is the keyword score.</summary>
    [Range(0.0, 1.0)]
    public double VectorWeight { get; init; } = 0.65;

    [Range(1, 128)]
    public int BatchSize { get; init; } = 16;
}
