using System.Diagnostics;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Lumen.Api.Mcp;

public static class McpToolErrorFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            var services = context.Services!;
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(McpToolErrorFilter));
            var tenantId = services.GetRequiredService<ICurrentTenant>().TenantId;
            var tool = context.Params?.Name;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await next(context, cancellationToken);
                logger.LogInformation("MCP tool {Tool} for tenant {TenantId} finished in {ElapsedMs} ms (isError: {IsError})",
                    tool, tenantId, stopwatch.ElapsedMilliseconds, result.IsError == true);
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not McpProtocolException)
            {
                if (ex is ValidationException or NotFoundException)
                {
                    logger.LogWarning("MCP tool {Tool} for tenant {TenantId} rejected the call: {ExceptionType} {Message}",
                        tool, tenantId, ex.GetType().Name, ex.Message);
                }
                else
                {
                    logger.LogError(ex, "MCP tool {Tool} for tenant {TenantId} failed after {ElapsedMs} ms",
                        tool, tenantId, stopwatch.ElapsedMilliseconds);
                }

                return ToResult(ex);
            }
        };

    public static CallToolResult ToResult(Exception exception) => new()
    {
        IsError = true,
        Content =
        [
            new TextContentBlock
            {
                Text = exception switch
                {
                    ValidationException => exception.Message,
                    NotFoundException => McpConstants.NotFoundText,
                    ExternalServiceException => McpConstants.UnavailableText,
                    _ => McpConstants.UnexpectedText
                }
            }
        ]
    };
}
