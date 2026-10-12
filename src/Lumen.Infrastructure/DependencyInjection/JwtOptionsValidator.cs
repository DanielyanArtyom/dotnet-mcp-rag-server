namespace Lumen.Infrastructure.DependencyInjection;

public class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public const int MinSecretBytes = 32;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (Encoding.UTF8.GetByteCount(options.Secret ?? string.Empty) < MinSecretBytes)
        {
            failures.Add($"Jwt:Secret must be set to at least {MinSecretBytes} bytes (HS256); set it with the Jwt__Secret environment variable.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Jwt:Issuer must be set.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Audience must be set.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
