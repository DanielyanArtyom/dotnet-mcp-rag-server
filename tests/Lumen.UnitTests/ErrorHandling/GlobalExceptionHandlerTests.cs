namespace Lumen.UnitTests.ErrorHandling;

public class GlobalExceptionHandlerTests
{
    private static readonly ServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .AddProblemDetails()
        .BuildServiceProvider();

    private static GlobalExceptionHandler CreateHandler() =>
        new(NullLogger<GlobalExceptionHandler>.Instance, Services.GetRequiredService<IProblemDetailsService>());

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext { RequestServices = Services };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    [Theory]
    [InlineData(typeof(NotFoundException), 404)]
    [InlineData(typeof(ForbiddenException), 403)]
    [InlineData(typeof(ConflictException), 409)]
    [InlineData(typeof(ValidationException), 400)]
    [InlineData(typeof(UnauthorizedException), 401)]
    public async Task TryHandleAsync_maps_each_exception_type_to_the_correct_status_code(Type exceptionType, int expectedStatus)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "test message")!;
        var context = CreateContext();

        var handled = await CreateHandler().TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);

        var body = await ReadBodyAsync(context);
        Assert.Contains("test message", body);
    }

    [Fact]
    public async Task TryHandleAsync_maps_unknown_exceptions_to_500_with_a_generic_message_not_the_original_details()
    {
        var exception = new InvalidOperationException("some internal detail that must not leak");
        var context = CreateContext();

        var handled = await CreateHandler().TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(500, context.Response.StatusCode);

        var body = await ReadBodyAsync(context);
        Assert.DoesNotContain("some internal detail that must not leak", body);
        Assert.Contains("An unexpected error occurred.", body);
    }

    [Fact]
    public async Task Maps_ExternalServiceException_to_503_without_leaking_the_message()
    {
        var context = CreateContext();

        var handled = await CreateHandler().TryHandleAsync(context, new ExternalServiceException("https://lumen.openai.azure.com returned 401"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);

        var body = await ReadBodyAsync(context);
        Assert.Contains("A dependency of this service is unavailable.", body);
        Assert.DoesNotContain("lumen.openai.azure.com", body);
    }
}
