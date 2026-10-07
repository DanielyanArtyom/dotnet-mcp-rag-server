using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
using Lumen.Infrastructure.Chat;
using OpenAI;

namespace Lumen.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLumenServices(this IServiceCollection services)
    {
        services.AddScoped<CurrentTenant>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());

        services.AddScoped<IRepository<Document>, DocumentRepository>();
        services.AddScoped<IDocumentChunkRepository, DocumentChunkRepository>();
        services.AddScoped<IChunkSearchRepository, ChunkSearchRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserLookupService, UserLookupService>();
        services.AddScoped<ITenantLookupService, TenantLookupService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<IIngestProfileProvider, IngestProfileProvider>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IDocumentReaderFactory, DocumentReaderFactory>();
        services.AddScoped<IChunker, Chunker>();
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IEmbeddingService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
            return options.Provider switch
            {
                EmbeddingProvider.AzureOpenAI => new AzureOpenAIEmbeddingService(
                    CreateAzureGenerator(options), options.Model, options.MaxBatchSize),
                _ => new FakeEmbeddingService()
            };
        });

        services.AddScoped<IChatService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ChatSettingsOptions>>().Value;
            IChatClient inner = options.Provider switch
            {
                ChatProvider.AzureOpenAI => CreateOpenAIClient(options.Endpoint, options.ApiKey)
                    .GetChatClient(options.Deployment).AsIChatClient(),
                _ => new FakeChatClient()
            };

            var client = new ChatClientBuilder(inner)
                .UseLogging(sp.GetRequiredService<ILoggerFactory>())
                .Build(sp);

            return new ChatService(client, options, sp.GetRequiredService<ILogger<ChatService>>());
        });

        return services;
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateAzureGenerator(EmbeddingOptions options) =>
        CreateOpenAIClient(options.Endpoint, options.ApiKey)
            .GetEmbeddingClient(options.Deployment)
            .AsIEmbeddingGenerator(EmbeddingConstants.Dimensions);

    private static OpenAIClient CreateOpenAIClient(string endpoint, string apiKey)
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };

        // Azure OpenAI's v1 API is reached with the plain OpenAI client; an empty ApiKey means Entra ID
        // (managed identity), whose BearerTokenPolicy overload is still marked experimental as OPENAI001.
#pragma warning disable OPENAI001
        return string.IsNullOrWhiteSpace(apiKey)
            ? new OpenAIClient(
                authenticationPolicy: new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"),
                options: clientOptions)
            : new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
#pragma warning restore OPENAI001
    }
}
