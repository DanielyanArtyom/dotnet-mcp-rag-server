using Lumen.Api.Mcp;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using Pgvector.EntityFrameworkCore;

namespace Lumen.Api.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder Configure(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<JwtOptions>()
            .Bind(builder.Configuration.GetSection(JwtClaimConstants.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
        builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));
        builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection(FileStorageOptions.SectionName));
        builder.Services.PostConfigure<FileStorageOptions>(o =>
        {
            if (!Path.IsPathRooted(o.RootPath))
                o.RootPath = Path.Combine(builder.Environment.ContentRootPath, o.RootPath);
        });
        builder.Services.AddOptions<ChunkingOptions>()
            .Bind(builder.Configuration.GetSection(ChunkingOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ChunkingOptions>, ChunkingOptionsValidator>();
        builder.Services.AddOptions<EmbeddingOptions>()
            .Bind(builder.Configuration.GetSection(EmbeddingOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<EmbeddingOptions>, EmbeddingOptionsValidator>();
        builder.Services.AddOptions<ChatSettingsOptions>()
            .Bind(builder.Configuration.GetSection(ChatSettingsOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ChatSettingsOptions>, ChatSettingsOptionsValidator>();
        builder.Services.AddOptions<SearchOptions>()
            .Bind(builder.Configuration.GetSection(SearchOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<SearchOptions>, SearchOptionsValidator>();
        builder.Services.AddOptions<McpOptions>()
            .Bind(builder.Configuration.GetSection(McpOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<McpOptions>, McpOptionsValidator>();

        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                builder.Configuration.GetConnectionString("Default") is { Length: > 0 } connectionString
                    ? connectionString
                    : throw new InvalidOperationException("ConnectionStrings:Default is not set; set it with the ConnectionStrings__Default environment variable."),
                o => o.UseVector()));

        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(LoginCommand).Assembly));

        return builder;
    }

    public static WebApplicationBuilder AddLogger(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Host.UseNLog();

        return builder;
    }

    public static WebApplicationBuilder AddWebApi(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddScoped<DemoFilingSeeder>();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>(tags: [HealthConstants.ReadyTag]);

        return builder;
    }

    public static WebApplicationBuilder AddMapper(this WebApplicationBuilder builder)
    {
        builder.Services.AddAutoMapper(cfg => { }, typeof(DomainEntityMappingProfile), typeof(DomainDtoMappingProfile));

        return builder;
    }

    public static WebApplicationBuilder AddAuthentication(this WebApplicationBuilder builder)
    {
        var jwtOptions = builder.Configuration.GetSection(JwtClaimConstants.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var validation = new JwtOptionsValidator().Validate(null, jwtOptions);
        if (validation.Failed)
        {
            throw new OptionsValidationException(JwtClaimConstants.SectionName, typeof(JwtOptions), validation.Failures!);
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
                    ValidateLifetime = true,
                    RoleClaimType = ClaimTypes.Role
                };
            });

        builder.Services.AddAuthorization();

        return builder;
    }

    public static WebApplicationBuilder AddMcp(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(McpOptions.SectionName).Get<McpOptions>() ?? new McpOptions();

        var mcp = builder.Services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation { Name = McpConstants.ServerName, Version = McpConstants.ServerVersion };
                o.ServerInstructions = McpConstants.ServerInstructions;
            })
            // Tenant isolation depends on Stateless: only then does a tool run in the HTTP request's DI scope, where
            // TenantResolutionMiddleware has set CurrentTenant. A stateful session would give tools an empty tenant.
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<FilingTools>()
            .WithRequestFilters(f => f.AddCallToolFilter(McpToolErrorFilter.Create));

        if (options.ContextTool)
        {
            mcp.WithTools<ChunkContextTool>();
        }

        return builder;
    }

    public static WebApplicationBuilder AddSwagger(this WebApplicationBuilder builder)
    {
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = Microsoft.OpenApi.ParameterLocation.Header,
                Description = "Paste the JWT returned by POST /api/auth/token."
            });
            options.AddSecurityRequirement(document => new Microsoft.OpenApi.OpenApiSecurityRequirement
            {
                [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
            });
        });

        return builder;
    }
}
