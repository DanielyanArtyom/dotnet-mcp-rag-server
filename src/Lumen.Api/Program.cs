using Lumen.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLumenServices();

builder
    .Configure()
    .AddLogger()
    .AddWebApi()
    .AddMapper()
    .AddSwagger()
    .AddAuthentication();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

await app.UseDatabaseMigrationAsync();

app.UseAuthentication();
app.UseMiddlewares();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
