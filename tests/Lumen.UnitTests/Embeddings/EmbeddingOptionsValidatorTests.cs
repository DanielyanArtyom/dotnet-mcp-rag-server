namespace Lumen.UnitTests.Embeddings;

public class EmbeddingOptionsValidatorTests
{
    private readonly EmbeddingOptionsValidator _validator = new();

    [Fact]
    public void Fake_provider_needs_no_azure_settings()
    {
        var result = _validator.Validate(null, new EmbeddingOptions { Provider = EmbeddingProvider.Fake });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Azure_provider_requires_an_endpoint_and_a_deployment()
    {
        var result = _validator.Validate(null, new EmbeddingOptions { Provider = EmbeddingProvider.AzureOpenAI });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Endpoint"));
        Assert.Contains(result.Failures, f => f.Contains("Deployment"));
    }

    [Fact]
    public void Azure_provider_without_an_api_key_is_valid_because_that_means_managed_identity()
    {
        var options = new EmbeddingOptions
        {
            Provider = EmbeddingProvider.AzureOpenAI,
            Endpoint = "https://lumen.openai.azure.com/openai/v1/",
            Deployment = "text-embedding-3-small",
            ApiKey = ""
        };

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2049)]
    public void Batch_size_must_be_within_the_services_array_limit(int batchSize)
    {
        var options = new EmbeddingOptions { Provider = EmbeddingProvider.Fake, MaxBatchSize = batchSize };

        Assert.True(_validator.Validate(null, options).Failed);
    }
}
