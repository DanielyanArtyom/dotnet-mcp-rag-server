namespace Lumen.Api.DependencyInjection;

public class McpOptionsValidator : IValidateOptions<McpOptions>
{
    public ValidateOptionsResult Validate(string? name, McpOptions options)
    {
        var invalid = options.AllowedOrigins
            .Where(o => !Uri.TryCreate(o, UriKind.Absolute, out var uri)
                        || uri.Scheme is not ("http" or "https")
                        || uri.AbsolutePath != "/")
            .ToList();

        return invalid.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Mcp:AllowedOrigins must be scheme://host[:port] values; invalid: {string.Join(", ", invalid)}");
    }
}
