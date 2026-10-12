using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLumenServices();

builder
    .Configure()
    .AddLogger()
    .AddWebApi()
    .AddMapper()
    .AddSwagger()
    .AddAuthentication()
    .AddMcp();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

await app.UseStartupDataAsync();

app.UseAuthentication();
app.UseMiddlewares();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks(HealthConstants.LivePath, new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks(HealthConstants.ReadyPath, new HealthCheckOptions { Predicate = c => c.Tags.Contains(HealthConstants.ReadyTag) });
app.MapMcp(McpConstants.EndpointPath).RequireAuthorization();

app.Run();

public partial class Program;
