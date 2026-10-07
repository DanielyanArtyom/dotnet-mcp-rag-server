namespace Lumen.Infrastructure.DependencyInjection;

public class ChunkingOptionsValidator : IValidateOptions<ChunkingOptions>
{
    public ValidateOptionsResult Validate(string? name, ChunkingOptions options)
    {
        var failures = new List<string>();

        if (options.MaxTokensPerChunk < 1)
        {
            failures.Add("Chunking:MaxTokensPerChunk must be positive.");
        }

        if (options.OverlapTokens < 0 || options.OverlapTokens >= options.MaxTokensPerChunk)
        {
            failures.Add("Chunking:OverlapTokens must be at least 0 and smaller than MaxTokensPerChunk.");
        }

        if (options.MergeBelowTokens < 0 || options.MergeBelowTokens >= options.MaxTokensPerChunk)
        {
            failures.Add("Chunking:MergeBelowTokens must be at least 0 (0 = off) and smaller than MaxTokensPerChunk.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
