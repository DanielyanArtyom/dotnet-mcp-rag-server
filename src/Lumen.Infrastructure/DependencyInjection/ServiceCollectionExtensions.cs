using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
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

        return services;
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateAzureGenerator(EmbeddingOptions options)
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(options.Endpoint) };

        // Azure OpenAI's v1 API is reached with the plain OpenAI client; an empty ApiKey means Entra ID
        // (managed identity), whose BearerTokenPolicy overload is still marked experimental as OPENAI001.
#pragma warning disable OPENAI001
        var client = string.IsNullOrWhiteSpace(options.ApiKey)
            ? new OpenAIClient(
                authenticationPolicy: new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"),
                options: clientOptions)
            : new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions);
#pragma warning restore OPENAI001

        return client.GetEmbeddingClient(options.Deployment).AsIEmbeddingGenerator(EmbeddingConstants.Dimensions);
    }
}
