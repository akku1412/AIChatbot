using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Services.Ai;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Tests.Unit;

public class AiModelCatalogTests
{
    private sealed class RecordingFactory : IAiProviderFactory
    {
        public List<AiModelOption> Created { get; } = [];

        public IAiChatProvider Create(AiModelOption model)
        {
            Created.Add(model);
            return new StubProvider(model.Id);
        }
    }

    private sealed class StubProvider(string name) : IAiChatProvider
    {
        public string Name => name;

        public Task<AiCompletion> CompleteAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static AiModelOption Model(string id, string provider = "Groq") =>
        new() { Id = id, Label = id.ToUpperInvariant(), Provider = provider, Model = $"{id}-model" };

    private static AiModelCatalog Catalog(AiOptions options, RecordingFactory? factory = null) =>
        new(Options.Create(options), factory ?? new RecordingFactory());

    [Fact]
    public void Without_a_list_the_active_provider_model_is_the_only_one()
    {
        var factory = new RecordingFactory();
        var catalog = Catalog(new AiOptions { Provider = "Groq", Groq = new OpenAiProviderOptions { Model = "openai/gpt-oss-120b", ReasoningEffort = "low" } }, factory);

        var described = catalog.Describe();
        Assert.Equal("default", Assert.Single(described.Models).Id);
        catalog.Provider(null);
        var model = Assert.Single(factory.Created);
        Assert.Equal(("Groq", "openai/gpt-oss-120b", "low"), (model.Provider, model.Model, model.ReasoningEffort));
    }

    [Fact]
    public void Unknown_ids_resolve_to_the_default_and_providers_are_created_once_per_model()
    {
        var factory = new RecordingFactory();
        var catalog = Catalog(new AiOptions { Models = [Model("fast"), Model("smart")], DefaultModel = "smart" }, factory);

        Assert.Equal("smart", catalog.Resolve(null).Id);
        Assert.Equal("smart", catalog.Resolve("gone").Id);
        Assert.Equal("fast", catalog.Resolve("fast").Id);
        Assert.False(catalog.IsAllowed("gone"));
        Assert.False(catalog.IsAllowed(null));

        Assert.Same(catalog.Provider("fast"), catalog.Provider("fast"));
        Assert.Same(catalog.Provider("gone"), catalog.Provider("smart"));
        Assert.Equal(["fast", "smart"], factory.Created.Select(m => m.Id));
    }

    [Fact]
    public void The_first_model_is_the_default_when_none_is_named()
    {
        Assert.Equal("fast", Catalog(new AiOptions { Models = [Model("fast"), Model("smart")] }).Describe().DefaultModelId);
    }

    public static TheoryData<AiOptions, string> BadConfigs => new()
    {
        { new AiOptions { Models = [Model("fast"), Model("fast")] }, "more than once" },
        { new AiOptions { Models = [Model("Fast Model")] }, "lower-case" },
        { new AiOptions { Models = [Model("fast", provider: "Anthropic")] }, "unknown Provider" },
        { new AiOptions { Models = [Model("fast")], DefaultModel = "smart" }, "DefaultModel" },
        { new AiOptions { Models = [new AiModelOption { Id = "fast", Label = "Fast", Provider = "Groq" }] }, "needs a Label and a Model" },
    };

    [Theory]
    [MemberData(nameof(BadConfigs))]
    public void Configuration_mistakes_fail_at_startup(AiOptions options, string message)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Catalog(options));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Factory_builds_a_provider_named_after_the_account_and_model_id()
    {
        var options = new AiOptions
        {
            Groq = new OpenAiProviderOptions { ApiKey = "k", VisionModel = "provider-vision" },
            Models = [new AiModelOption { Id = "smart", Label = "Smart", Provider = "Groq", Model = "multimodal", SupportsVision = true }],
        };
        var factory = new OpenAiProviderFactory(Options.Create(options), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        Assert.Equal("Groq/smart", factory.Create(options.Models[0]).Name);
    }
}
