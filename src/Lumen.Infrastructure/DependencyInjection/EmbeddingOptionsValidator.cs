namespace Lumen.Infrastructure.DependencyInjection;

public class EmbeddingOptionsValidator : IValidateOptions<EmbeddingOptions>
{
    public ValidateOptionsResult Validate(string? name, EmbeddingOptions options)
    {
        var failures = new List<string>();

        if (options.MaxBatchSize is < 1 or > 2048)
        {
            failures.Add("Embeddings:MaxBatchSize must be between 1 and 2048 (the embeddings API accepts at most 2048 inputs per call).");
        }

        if (options.Provider == EmbeddingProvider.AzureOpenAI)
        {
            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                failures.Add("Embeddings:Endpoint is required when Embeddings:Provider is AzureOpenAI (for example https://<resource>.openai.azure.com/openai/v1/).");
            }

            if (string.IsNullOrWhiteSpace(options.Deployment))
            {
                failures.Add("Embeddings:Deployment is required when Embeddings:Provider is AzureOpenAI.");
            }

            if (string.IsNullOrWhiteSpace(options.Model))
            {
                failures.Add("Embeddings:Model is required when Embeddings:Provider is AzureOpenAI.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
