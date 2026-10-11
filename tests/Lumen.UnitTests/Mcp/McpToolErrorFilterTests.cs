using ModelContextProtocol.Protocol;

namespace Lumen.UnitTests.Mcp;

public class McpToolErrorFilterTests
{
    private static string TextOf(CallToolResult result) => Assert.Single(result.Content.OfType<TextContentBlock>()).Text;

    [Fact]
    public void Validation_errors_pass_their_message_through()
    {
        var result = McpToolErrorFilter.ToResult(new Lumen.Application.Exceptions.ValidationException("topK must be between 1 and 20."));

        Assert.True(result.IsError);
        Assert.Equal("topK must be between 1 and 20.", TextOf(result));
    }

    [Fact]
    public void Not_found_points_the_agent_at_the_listing_tools()
    {
        var result = McpToolErrorFilter.ToResult(new NotFoundException("Chunk 123 was not found."));

        Assert.True(result.IsError);
        Assert.Equal(McpConstants.NotFoundText, TextOf(result));
    }

    [Fact]
    public void Provider_outage_is_reported_as_retryable()
    {
        var result = McpToolErrorFilter.ToResult(new ExternalServiceException("Generating an embedding failed.", new HttpRequestException("503")));

        Assert.Equal(McpConstants.UnavailableText, TextOf(result));
    }

    [Fact]
    public void Unexpected_exceptions_leak_no_details()
    {
        var result = McpToolErrorFilter.ToResult(new InvalidOperationException("connection string Host=secret"));

        Assert.True(result.IsError);
        Assert.Equal(McpConstants.UnexpectedText, TextOf(result));
    }
}
