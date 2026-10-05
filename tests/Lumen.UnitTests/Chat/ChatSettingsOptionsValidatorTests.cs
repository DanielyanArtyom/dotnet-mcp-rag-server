namespace Lumen.UnitTests.Chat;

public class ChatSettingsOptionsValidatorTests
{
    private readonly ChatSettingsOptionsValidator _validator = new();

    [Fact]
    public void Fake_provider_with_defaults_is_valid() =>
        Assert.True(_validator.Validate(null, new ChatSettingsOptions()).Succeeded);

    [Fact]
    public void Azure_provider_requires_an_endpoint_and_a_deployment()
    {
        var result = _validator.Validate(null, new ChatSettingsOptions { Provider = ChatProvider.AzureOpenAI });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Endpoint"));
        Assert.Contains(result.Failures, f => f.Contains("Deployment"));
    }

    [Fact]
    public void Azure_provider_without_an_api_key_is_valid_because_that_means_managed_identity()
    {
        var options = new ChatSettingsOptions
        {
            Provider = ChatProvider.AzureOpenAI,
            Endpoint = "https://lumen.services.ai.azure.com/openai/v1/",
            Deployment = "gpt-answer"
        };

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Context_top_k_must_be_between_1_and_20(int topK) =>
        Assert.True(_validator.Validate(null, new ChatSettingsOptions { ContextTopK = topK }).Failed);

    [Fact]
    public void Max_output_tokens_must_be_positive() =>
        Assert.True(_validator.Validate(null, new ChatSettingsOptions { MaxOutputTokens = 0 }).Failed);

    [Theory]
    [InlineData(-0.1f, null)]
    [InlineData(2.1f, null)]
    [InlineData(null, 0f)]
    [InlineData(null, 1.1f)]
    public void Sampling_values_must_be_in_range_when_set(float? temperature, float? topP) =>
        Assert.True(_validator.Validate(null, new ChatSettingsOptions { Temperature = temperature, TopP = topP }).Failed);
}
