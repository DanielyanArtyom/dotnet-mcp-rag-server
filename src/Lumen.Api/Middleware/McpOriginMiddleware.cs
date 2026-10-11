namespace Lumen.Api.Middleware;

public class McpOriginMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _allowed;
    private readonly ILogger<McpOriginMiddleware> _logger;

    public McpOriginMiddleware(RequestDelegate next, IOptions<McpOptions> options, ILogger<McpOriginMiddleware> logger)
    {
        _next = next;
        _allowed = options.Value.AllowedOrigins.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(McpConstants.EndpointPath)
            && context.Request.Headers.Origin is { Count: > 0 } origin
            && !_allowed.Contains(Normalize(origin.ToString())))
        {
            _logger.LogWarning("Rejected MCP request from origin {Origin}", origin.ToString());
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }

    private static string Normalize(string origin) => origin.Trim().TrimEnd('/');
}
