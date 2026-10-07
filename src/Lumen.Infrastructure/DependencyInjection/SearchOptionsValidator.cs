namespace Lumen.Infrastructure.DependencyInjection;

public class SearchOptionsValidator : IValidateOptions<SearchOptions>
{
    public ValidateOptionsResult Validate(string? name, SearchOptions options)
    {
        var failures = new List<string>();

        if (options.RrfK < 1)
        {
            failures.Add("Search:RrfK must be positive.");
        }

        if (options.Candidates is < 1 or > 200)
        {
            failures.Add("Search:Candidates must be between 1 and 200.");
        }

        if (!double.IsFinite(options.LexicalWeight) || !double.IsFinite(options.VectorWeight)
            || options.LexicalWeight < 0 || options.VectorWeight < 0 || options.LexicalWeight + options.VectorWeight <= 0)
        {
            failures.Add("Search:LexicalWeight and Search:VectorWeight must be non-negative and not both zero.");
        }

        if (options.MaxChunksPerDocument is < 1)
        {
            failures.Add("Search:MaxChunksPerDocument must be at least 1 when set.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
