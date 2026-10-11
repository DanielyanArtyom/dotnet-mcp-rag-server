using Lumen.Api.Middleware;

namespace Lumen.UnitTests.Mcp;

public class McpOriginMiddlewareTests
{
    private bool _nextCalled;

    private McpOriginMiddleware Create(params string[] allowed) => new(
        _ => { _nextCalled = true; return Task.CompletedTask; },
        Options.Create(new McpOptions { AllowedOrigins = allowed }),
        NullLogger<McpOriginMiddleware>.Instance);

    private static DefaultHttpContext Request(string path, string? origin)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        return context;
    }

    [Fact]
    public async Task No_origin_header_passes()
    {
        var context = Request("/mcp", null);
        await Create().InvokeAsync(context);
        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task Allowed_origin_passes_ignoring_case_and_trailing_slash()
    {
        var context = Request("/mcp", "HTTP://localhost:6274/");
        await Create("http://localhost:6274").InvokeAsync(context);
        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task Foreign_origin_on_mcp_is_forbidden()
    {
        var context = Request("/mcp", "https://evil.example");
        await Create("http://localhost:6274").InvokeAsync(context);
        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Other_paths_are_untouched()
    {
        var context = Request("/api/search", "https://evil.example");
        await Create().InvokeAsync(context);
        Assert.True(_nextCalled);
    }
}
