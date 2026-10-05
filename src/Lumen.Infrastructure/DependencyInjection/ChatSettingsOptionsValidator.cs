namespace Lumen.Infrastructure.DependencyInjection;

public class ChatSettingsOptionsValidator : IValidateOptions<ChatSettingsOptions>
{
    public ValidateOptionsResult Validate(string? name, ChatSettingsOptions options)
    {
        var failures = new List<string>();

        if (options.ContextTopK is < 1 or > 20)
        {
            failures.Add("Chat:ContextTopK must be between 1 and 20.");
        }

        if (options.MaxOutputTokens < 1)
        {
            failures.Add("Chat:MaxOutputTokens must be positive.");
        }

        if (options.Temperature is < 0f or > 2f)
        {
            failures.Add("Chat:Temperature must be between 0 and 2 when set.");
        }

        if (options.TopP is <= 0f or > 1f)
        {
            failures.Add("Chat:TopP must be greater than 0 and at most 1 when set.");
        }

        if (options.Provider == ChatProvider.AzureOpenAI)
        {
            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                failures.Add("Chat:Endpoint is required when Chat:Provider is AzureOpenAI (for example https://<resource>.services.ai.azure.com/openai/v1/).");
            }

            if (string.IsNullOrWhiteSpace(options.Deployment))
            {
                failures.Add("Chat:Deployment is required when Chat:Provider is AzureOpenAI.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
