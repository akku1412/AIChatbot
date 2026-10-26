using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services.Ai;

/// <summary>A model as clients see it: an id and a label, never the provider's model name or keys.</summary>
public sealed record AiModelDto(string Id, string Label, string? Description, bool SupportsVision);

/// <summary>GET /api/models: what the picker offers, and which one new chats use.</summary>
public sealed record AiModelsDto(IReadOnlyList<AiModelDto> Models, string DefaultModelId);

/// <summary>Creates the provider that talks to one configured model (tests replace this with a fake).</summary>
public interface IAiProviderFactory
{
    IAiChatProvider Create(AiModelOption model);
}

public sealed class OpenAiProviderFactory(IOptions<AiOptions> options, ILoggerFactory loggers) : IAiProviderFactory
{
    public IAiChatProvider Create(AiModelOption model)
    {
        var ai = options.Value;
        var account = ai.ProviderSettings(model.Provider);
        var settings = new OpenAiProviderOptions
        {
            ApiKey = account.ApiKey,
            Endpoint = account.Endpoint,
            Model = model.Model,
            ReasoningEffort = model.ReasoningEffort,
            VisionModel = model.SupportsVision ? model.Model : account.VisionModel,
        };
        return new OpenAiChatProvider($"{model.Provider}/{model.Id}", settings, ai.TimeoutSeconds,
            loggers.CreateLogger<OpenAiChatProvider>());
    }
}

/// <summary>
/// The allow-list of chat models (Ai:Models). Every lookup goes through it, so a conversation can only ever run a
/// configured model; an id that isn't (or is no longer) configured falls back to the default model.
/// </summary>
public interface IAiModelCatalog
{
    AiModelsDto Describe();

    /// <summary>True if <paramref name="modelId"/> is on the allow-list.</summary>
    bool IsAllowed(string? modelId);

    /// <summary>The configured model for this id, or the default for null/unknown ids.</summary>
    AiModelOption Resolve(string? modelId);

    /// <summary>The provider for <see cref="Resolve"/>(<paramref name="modelId"/>); one instance per model.</summary>
    IAiChatProvider Provider(string? modelId);
}

public sealed partial class AiModelCatalog : IAiModelCatalog
{
    private readonly IReadOnlyList<AiModelOption> _models;
    private readonly AiModelOption _default;
    private readonly IAiProviderFactory _factory;
    private readonly ConcurrentDictionary<string, IAiChatProvider> _providers = new(StringComparer.Ordinal);

    public AiModelCatalog(IOptions<AiOptions> options, IAiProviderFactory factory)
    {
        _factory = factory;
        var ai = options.Value;
        _models = ai.Models.Count > 0
            ? ai.Models
            :
            [
                // No picker configured: behave exactly as before, with the active provider's model.
                new AiModelOption
                {
                    Id = "default",
                    Label = "Default",
                    Provider = ai.Provider,
                    Model = ai.Active.Model,
                    ReasoningEffort = ai.Active.ReasoningEffort,
                },
            ];
        Validate(_models);

        if (string.IsNullOrWhiteSpace(ai.DefaultModel))
        {
            _default = _models[0];
        }
        else
        {
            _default = _models.FirstOrDefault(m => m.Id == ai.DefaultModel)
                ?? throw new InvalidOperationException($"Ai:DefaultModel '{ai.DefaultModel}' is not one of Ai:Models.");
        }
    }

    public AiModelsDto Describe() => new(
        _models.Select(m => new AiModelDto(m.Id, m.Label, m.Description, m.SupportsVision)).ToList(),
        _default.Id);

    public bool IsAllowed(string? modelId) => modelId is not null && _models.Any(m => m.Id == modelId);

    public AiModelOption Resolve(string? modelId) => _models.FirstOrDefault(m => m.Id == modelId) ?? _default;

    public IAiChatProvider Provider(string? modelId)
    {
        var model = Resolve(modelId);
        return _providers.GetOrAdd(model.Id, _ => _factory.Create(model));
    }

    /// <summary>Configuration mistakes fail at startup, not on a user's first message.</summary>
    private static void Validate(IReadOnlyList<AiModelOption> models)
    {
        foreach (var model in models)
        {
            if (!IdPattern().IsMatch(model.Id))
            {
                throw new InvalidOperationException($"Ai:Models id '{model.Id}' must be lower-case letters, digits or '-' (max 32).");
            }
            if (string.IsNullOrWhiteSpace(model.Label) || string.IsNullOrWhiteSpace(model.Model))
            {
                throw new InvalidOperationException($"Ai:Models '{model.Id}' needs a Label and a Model.");
            }
            if (model.Provider is not ("OpenAI" or "Groq"))
            {
                throw new InvalidOperationException($"Ai:Models '{model.Id}' has unknown Provider '{model.Provider}' (use OpenAI or Groq).");
            }
        }
        var duplicate = models.GroupBy(m => m.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Ai:Models id '{duplicate.Key}' is used more than once.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$")]
    private static partial Regex IdPattern();
}
