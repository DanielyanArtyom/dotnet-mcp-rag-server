# Stage 6A — Run anywhere with `docker compose up`: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** a fresh clone plus `.env` plus `docker compose up --build` gives a working Lumen on `localhost:5119`. It has a migrated database, the demo tenants and the four EDGAR filings ingested, `/health/live|ready`, and Swagger. Secrets and hosts come from the environment, and a missing secret fails fast.

**Architecture:**
- Startup steps (migrate, seed users, seed filings) run behind flags in `UseStartupDataAsync`. The filing seeder sends the existing `UploadDocumentCommand` through MediatR for each entry of `samples/edgar/manifest.json`.
- Health checks use `AddDbContextCheck`.
- A multi-stage Dockerfile produces a chiseled-extra non-root image. Compose wires API + pgvector with a Postgres healthcheck, a `/data` volume and a read-only `/seed` mount.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10 + Npgsql, MediatR, NLog, Docker / compose, xUnit + Testcontainers.

**Spec:** `.research/stage-6/design-6a.md`. Research: `.research/stage-6/run-anywhere-containers.md`. Read the "Pre-implementation corrections" first; they override the spec where they differ.

## Global Constraints

- **Never run `git commit` or `git push`.** Stage with `git add`. The user commits by hand.
- Today's behaviour stays: `dotnet run` in Development and the integration test host (which runs as Development) still migrate and seed users at startup, and need no new settings.
- New flags default to **off** in code and in `appsettings.json`: `Database:MigrateOnStartup=false`, `Database:SeedDemoData=false`, `Demo:FilingsPath=""`, `Swagger:Enabled` default = `IsDevelopment()`. `appsettings.Development.json` sets `Database:MigrateOnStartup=true` and `Database:SeedDemoData=true`.
- A missing or short (< 32 UTF-8 bytes) `Jwt:Secret` stops startup with a message containing `Jwt:Secret`. A missing `ConnectionStrings:Default` fails with a message containing `ConnectionStrings:Default`.
- Container: runtime `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`, non-root (`$APP_UID`), port 8080 published as `5119:8080`.
- Demo filings: titles and dates exactly `Apple 10-K` 2025-09-27, `Apple 10-Q` 2026-06-27 (tenant `Acme Corp`); `Morgan 10-K` 2025-12-31, `Morgan 10-Q` 2026-06-30 (tenant `Globex Inc`).
- The integration test host pins `Demo:FilingsPath` to empty.
- Conventions (CLAUDE.md):
  - no narrating comments;
  - Options classes live in `DependencyInjection/`;
  - DI classes use a classic constructor + `private readonly` fields;
  - `GlobalUsings.cs` rules: promote a namespace used in 2+ files, keep the 4 blocks in order, no unused usings;
  - DTOs only in `Lumen.Api/DTO`;
  - repeated magic strings become constants.
- Build has 0 warnings. `IgnoreQueryFilters` stays only in `UserLookupService` in `src` (tests may use it).

## Pre-implementation corrections (override the spec)

1. **NLog switch key.** Use `Logs:FileMinLevel` (env `Logs__FileMinLevel=Off`) instead of the spec's `Logging__File__Enabled`, so we don't put an unknown provider name inside the Microsoft `Logging` section. The file rule reads `minlevel="${configsetting:item=Logs.FileMinLevel:whenEmpty=Info}"`.
2. **Connection-string check is lazy.** It runs inside the `AddDbContext` options lambda. With `MigrateOnStartup` on, that is still startup, and it stays robust whatever order `WebApplicationFactory` settings arrive in. The JWT check is eager (`AddAuthentication` already reads the section eagerly, and `McpEndpointTests` show that `UseSetting` values are visible there).
3. **Raw-file persistence check** in the smoke script uses `docker run --rm -v <volume>:/data alpine ls -ln /data/documents` (the API image has no shell). It also verifies UID 1654 ownership. There is no download endpoint.
4. **Smoke isolation.** The developer's compose project already owns volume `lumen-postgres-data` with the Stage 6B corpus (Azure embeddings). The smoke script therefore runs under its own project name (`lumen-smoke`) and ports (55119/55432, via `API_PORT`/`POSTGRES_PORT` in compose) and deletes its own volumes on exit.

## Review Focus

1. **The test host and `dotnet run` (Development) must keep migrating and seeding** without new settings; a missed `appsettings.Development.json` flag breaks every integration test. Covered by the full integration suite in Task 1.
2. **Demo seeding against an existing corpus** (local dev DB that already has the four titles) must be a no-op with no duplicate upload. Test in Task 4.
3. **A manifest entry for an unknown tenant, or a missing file** must stop startup with a message naming the entry. Running Fake ingestion half-way is not acceptable. Tests in Task 4.
4. **`/health/*` must answer without a token**, including through `TenantResolutionMiddleware` and `McpOriginMiddleware`, and `/health/ready` must return 503 (not throw) when the DB is down. Tests in Task 3.
5. **An empty `JWT_SECRET` in `.env`** must stop `docker compose up` (compose `:?`), and an empty `Jwt:Secret` must stop the app with the setting named. Tests in Task 2; smoke in Task 5.

---

## File map

| File | Status | Responsibility |
|---|---|---|
| `src/Lumen.Api/DependencyInjection/DatabaseOptions.cs`, `DemoOptions.cs` | create | startup flags |
| `src/Lumen.Api/Extensions/ApplicationBuilderExtension.cs` | modify | `UseStartupDataAsync` (migrate → seed users → seed filings) |
| `src/Lumen.Api/Program.cs` | modify | `UseStartupDataAsync`, `Swagger:Enabled`, health endpoints |
| `src/Lumen.Infrastructure/Persistence/DbSeeder.cs` | modify | tenants + admins only |
| `src/Lumen.Infrastructure/DependencyInjection/JwtOptionsValidator.cs` | create | `Jwt:*` validation |
| `src/Lumen.Api/Extensions/WebApplicationBuilderExtensions.cs` | modify | options binding, JWT fail-fast, connection-string check, health checks, seeder DI |
| `src/Lumen.Api/Seeding/DemoManifest.cs`, `DemoFiling.cs`, `DemoFilingSeeder.cs` | create | manifest parsing and validation, seeding through `UploadDocumentCommand` |
| `src/Lumen.Api/Lumen.Api.csproj` | modify | `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.12 |
| `src/Lumen.Api/appsettings.json`, `appsettings.Development.json`, `nlog.config` | modify | flags, file-log switch |
| `samples/edgar/manifest.json` | create | the four filings |
| `Dockerfile`, `.dockerignore`, `docker-compose.yml`, `.env.example`, `.gitignore` | create/modify | container |
| `tools/compose-smoke.sh` | create | end-to-end check |
| tests (unit + integration) | create/modify | see tasks |
| `README.md`, `CLAUDE.md` | modify | docs |

---

### Task 1: Startup flags, user-only seeder, `Swagger:Enabled`

**Files:**
- Create: `src/Lumen.Api/DependencyInjection/DatabaseOptions.cs`, `src/Lumen.Api/DependencyInjection/DemoOptions.cs`
- Modify: `src/Lumen.Api/Extensions/ApplicationBuilderExtension.cs`, `src/Lumen.Api/Program.cs`, `src/Lumen.Api/Extensions/WebApplicationBuilderExtensions.cs` (`Configure`), `src/Lumen.Infrastructure/Persistence/DbSeeder.cs`, `src/Lumen.Api/appsettings.json`, `src/Lumen.Api/appsettings.Development.json`, `tests/Lumen.IntegrationTests/Utils/TestWebApplicationFactory.cs`
- Test: `tests/Lumen.IntegrationTests/Persistence/DbSeederTests.cs`, new `tests/Lumen.IntegrationTests/Settings/StartupFlagsTests.cs`

**Interfaces:**
- Produces:
  - `DatabaseOptions { const SectionName = "Database"; bool MigrateOnStartup; bool SeedDemoData }`;
  - `DemoOptions { const SectionName = "Demo"; string FilingsPath = "" }`;
  - `WebApplication.UseStartupDataAsync()`, which replaces `UseDatabaseMigrationAsync`. Task 4 adds the filing step to it.

- [ ] **Step 1: Failing tests.** Update `DbSeederTests`: rename it to `SeedAsync_creates_two_tenants_with_one_admin_each_and_no_documents_and_is_idempotent`, and replace the two `Welcome` assertions with:

```csharp
        var tenantIds = await verify.Tenants.Where(t => t.Name == "Acme Corp" || t.Name == "Globex Inc").Select(t => t.Id).ToListAsync();
        Assert.Equal(0, await verify.Documents.IgnoreQueryFilters().CountAsync(d => tenantIds.Contains(d.TenantId)));
```

Create `StartupFlagsTests` (collection `"Postgres collection"`, same style as `SettingsEndpointTests`):

```csharp
namespace Lumen.IntegrationTests.Settings;

[Collection("Postgres collection")]
public class StartupFlagsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Swagger_is_served_in_Development_by_default()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }

    [Fact]
    public async Task Swagger_can_be_switched_off()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.UseSetting("Swagger:Enabled", "false"));
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }

    [Fact]
    public async Task Users_are_seeded_when_SeedDemoData_is_on()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new { email = "admin@acme.test", password = "Passw0rd!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

If the token endpoint's request DTO differs, use the field names `TestAuth` uses.

- [ ] **Step 2: Run to verify failure.** `DOCKER_API_VERSION=1.43 dotnet test tests/Lumen.IntegrationTests --filter "FullyQualifiedName~DbSeederTests|FullyQualifiedName~StartupFlagsTests"`. Expected: `DbSeederTests` fails (Welcome documents exist), and `Swagger_can_be_switched_off` fails (Swagger is still on).

- [ ] **Step 3: Implement.** `DatabaseOptions.cs`:

```csharp
namespace Lumen.Api.DependencyInjection;

public class DatabaseOptions
{
    public const string SectionName = "Database";
    public bool MigrateOnStartup { get; set; }
    public bool SeedDemoData { get; set; }
}
```

`DemoOptions.cs`:

```csharp
namespace Lumen.Api.DependencyInjection;

public class DemoOptions
{
    public const string SectionName = "Demo";
    public string FilingsPath { get; set; } = string.Empty;
}
```

In `Configure()` add:

```csharp
        builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
        builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));
```

Replace `UseDatabaseMigrationAsync`:

```csharp
    public static async Task UseStartupDataAsync(this WebApplication app)
    {
        var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (database.MigrateOnStartup)
        {
            await db.Database.MigrateAsync();
        }

        if (database.SeedDemoData)
        {
            var seedTenant = scope.ServiceProvider.GetRequiredService<CurrentTenant>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            await DbSeeder.SeedAsync(db, seedTenant, hasher);
        }
    }
```

In `Program.cs`, use `await app.UseStartupDataAsync();` and gate Swagger:

```csharp
if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

`DbSeeder`:
- remove the document creation: drop `documentTitle`/`documentContent` from `SeedTenantAsync` and its callers, and drop the `context.Documents.Add(...)` block;
- keep tenants and admins, and keep the idempotency guard.

Settings files:
- `appsettings.json`: add `"Database": { "MigrateOnStartup": false, "SeedDemoData": false }` and `"Demo": { "FilingsPath": "" }`.
- `appsettings.Development.json`: add `"Database": { "MigrateOnStartup": true, "SeedDemoData": true }`.
- `TestWebApplicationFactory`: add `builder.UseSetting("Demo:FilingsPath", "");`.

- [ ] **Step 4: Run.** Unit suite plus the **full** integration suite (Review Focus 1). Expected: all green.

- [ ] **Step 5: Stage** (no commit).

---

### Task 2: Fail fast on missing secrets

**Files:**
- Create: `src/Lumen.Infrastructure/DependencyInjection/JwtOptionsValidator.cs`
- Modify: `src/Lumen.Api/Extensions/WebApplicationBuilderExtensions.cs` (`Configure`, `AddAuthentication`)
- Test: `tests/Lumen.UnitTests/Auth/JwtOptionsValidatorTests.cs`, new `tests/Lumen.IntegrationTests/Settings/StartupFailureTests.cs`

**Interfaces:**
- Produces `JwtOptionsValidator : IValidateOptions<JwtOptions>`, with failure messages naming `Jwt:Secret`, `Jwt:Issuer` and `Jwt:Audience`.

- [ ] **Step 1: Failing unit tests** (`JwtOptionsValidatorTests`):

```csharp
namespace Lumen.UnitTests.Auth;

public class JwtOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(string? secret, string issuer = "lumen", string audience = "lumen-clients") =>
        new JwtOptionsValidator().Validate(null, new JwtOptions { Secret = secret!, Issuer = issuer, Audience = audience });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short-secret")]
    public void A_missing_or_short_secret_fails_and_names_the_setting(string? secret)
    {
        var result = Validate(secret);

        Assert.True(result.Failed);
        Assert.Contains("Jwt:Secret", result.FailureMessage);
    }

    [Fact]
    public void A_32_byte_secret_with_issuer_and_audience_passes() =>
        Assert.True(Validate(new string('x', 32)).Succeeded);

    [Fact]
    public void Missing_issuer_or_audience_fails()
    {
        Assert.Contains("Jwt:Issuer", Validate(new string('x', 32), issuer: "").FailureMessage);
        Assert.Contains("Jwt:Audience", Validate(new string('x', 32), audience: "").FailureMessage);
    }
}
```

Integration `StartupFailureTests`:

```csharp
namespace Lumen.IntegrationTests.Settings;

[Collection("Postgres collection")]
public class StartupFailureTests(PostgresFixture fixture)
{
    [Fact]
    public void An_empty_jwt_secret_stops_startup_and_names_the_setting()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.UseSetting("Jwt:Secret", ""));

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("Jwt:Secret", error!.ToString());
    }

    [Fact]
    public void A_missing_connection_string_stops_startup_and_names_the_setting()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", ""));

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("ConnectionStrings:Default", error!.ToString());
    }
}
```

- [ ] **Step 2: Run to verify failure.** Unit: compile error. Integration: the exception message lacks the setting name.

- [ ] **Step 3: Implement.** `JwtOptionsValidator.cs`:

```csharp
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
```

In `Configure()`, replace `Configure<JwtOptions>` with:

```csharp
        builder.Services.AddOptions<JwtOptions>()
            .Bind(builder.Configuration.GetSection(JwtClaimConstants.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
```

Connection string, inside the existing `AddDbContext` lambda:

```csharp
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                builder.Configuration.GetConnectionString("Default") is { Length: > 0 } connectionString
                    ? connectionString
                    : throw new InvalidOperationException("ConnectionStrings:Default is not set; set it with the ConnectionStrings__Default environment variable."),
                o => o.UseVector()));
```

`AddAuthentication` reads the section eagerly, so validate there before building the key:

```csharp
        var jwtOptions = builder.Configuration.GetSection(JwtClaimConstants.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var validation = new JwtOptionsValidator().Validate(null, jwtOptions);
        if (validation.Failed)
        {
            throw new OptionsValidationException(JwtClaimConstants.SectionName, typeof(JwtOptions), validation.Failures!);
        }
```

The rest of `AddAuthentication` is unchanged.

- [ ] **Step 4: Run.** Unit suite and the full integration suite: all green.

- [ ] **Step 5: Stage.**

---

### Task 3: `/health/live` and `/health/ready`

**Files:**
- Modify: `src/Lumen.Api/Lumen.Api.csproj` (add `<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="10.0.12" />`), `src/Lumen.Api/Extensions/WebApplicationBuilderExtensions.cs` (`AddWebApi`), `src/Lumen.Api/Program.cs`
- Create: `src/Lumen.Api/Constants/HealthConstants.cs`
- Test: `tests/Lumen.IntegrationTests/Settings/HealthEndpointTests.cs`

**Interfaces:**
- Produces:
  - `HealthConstants.LivePath = "/health/live"`;
  - `HealthConstants.ReadyPath = "/health/ready"`;
  - `HealthConstants.ReadyTag = "ready"`.

- [ ] **Step 1: Failing tests:**

```csharp
namespace Lumen.IntegrationTests.Settings;

[Collection("Postgres collection")]
public class HealthEndpointTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_answer_anonymously(string path)
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_is_503_and_live_is_200_when_the_database_is_unreachable()
    {
        using var factory = TestWebApplicationFactory.Create("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2")
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Database:MigrateOnStartup", "false");
                b.UseSetting("Database:SeedDemoData", "false");
            });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
}
```

- [ ] **Step 2: Run to verify failure** (404s).

- [ ] **Step 3: Implement.** `HealthConstants.cs` in `Lumen.Api/Constants`:

```csharp
namespace Lumen.Api.Constants;

public static class HealthConstants
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";
    public const string ReadyTag = "ready";
}
```

In `AddWebApi()`:

```csharp
        builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>(tags: [HealthConstants.ReadyTag]);
```

In `Program.cs`, after `app.MapControllers();`:

```csharp
app.MapHealthChecks(HealthConstants.LivePath, new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks(HealthConstants.ReadyPath, new HealthCheckOptions { Predicate = c => c.Tags.Contains(HealthConstants.ReadyTag) });
```

`HealthCheckOptions` lives in `Microsoft.AspNetCore.Diagnostics.HealthChecks`. Add a local `using` in `Program.cs` (one file only).

- [ ] **Step 4: Run.** Full integration suite: green.

- [ ] **Step 5: Stage.**

---

### Task 4: Demo filing seeder + `samples/edgar/manifest.json`

**Files:**
- Create: `src/Lumen.Api/Seeding/DemoFiling.cs`, `src/Lumen.Api/Seeding/DemoManifest.cs`, `src/Lumen.Api/Seeding/DemoFilingSeeder.cs`, `samples/edgar/manifest.json`
- Modify: `src/Lumen.Api/Extensions/ApplicationBuilderExtension.cs` (`UseStartupDataAsync`), `src/Lumen.Api/Extensions/WebApplicationBuilderExtensions.cs` (register the seeder), `src/Lumen.Api/GlobalUsings.cs` (if `Lumen.Api.Seeding` is used in 2+ files)
- Test: `tests/Lumen.UnitTests/Seeding/DemoManifestTests.cs`, `tests/Lumen.IntegrationTests/Documents/DemoFilingSeederTests.cs`

**Interfaces:**
- Consumes: `DemoOptions.FilingsPath` (Task 1), `UploadDocumentCommand(string FileName, Stream Content, string Title, DateOnly FilingDate)`, `TenantScopedWriter.RunAsync(CurrentTenant, Guid, Func<Task>)`.
- Produces:
  - `record DemoFiling(string Tenant, string Title, DateOnly FilingDate, string File)`;
  - `DemoManifest.Load(string folder) : IReadOnlyList<DemoFiling>`, which throws `InvalidOperationException` with a message naming the folder or entry;
  - `DemoFilingSeeder.SeedAsync(string folder, CancellationToken ct)`.

- [ ] **Step 1: Manifest.** Create `samples/edgar/manifest.json`:

```json
{
  "filings": [
    { "tenant": "Acme Corp", "title": "Apple 10-K", "filingDate": "2025-09-27", "file": "aapl-20250927.html" },
    { "tenant": "Acme Corp", "title": "Apple 10-Q", "filingDate": "2026-06-27", "file": "aapl-20260627.html" },
    { "tenant": "Globex Inc", "title": "Morgan 10-K", "filingDate": "2025-12-31", "file": "jpm-20251231.html" },
    { "tenant": "Globex Inc", "title": "Morgan 10-Q", "filingDate": "2026-06-30", "file": "jpm-20260630.html" }
  ]
}
```

- [ ] **Step 2: Failing unit tests** (`DemoManifestTests`; each test writes into its own temp directory):

```csharp
namespace Lumen.UnitTests.Seeding;

public class DemoManifestTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lumen-manifest-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void WriteManifest(string json) => File.WriteAllText(Path.Combine(_folder, "manifest.json"), json);

    [Fact]
    public void Loads_entries_with_their_dates()
    {
        File.WriteAllText(Path.Combine(_folder, "a.html"), "<html></html>");
        WriteManifest("""{ "filings": [ { "tenant": "Acme Corp", "title": "Apple 10-K", "filingDate": "2025-09-27", "file": "a.html" } ] }""");

        var filing = Assert.Single(DemoManifest.Load(_folder));

        Assert.Equal(new DemoFiling("Acme Corp", "Apple 10-K", new DateOnly(2025, 9, 27), "a.html"), filing);
    }

    [Fact]
    public void A_folder_without_a_manifest_is_an_error_that_names_the_folder() =>
        Assert.Contains(_folder, Assert.Throws<InvalidOperationException>(() => DemoManifest.Load(_folder)).Message);

    [Fact]
    public void A_missing_file_is_an_error_that_names_it()
    {
        WriteManifest("""{ "filings": [ { "tenant": "Acme Corp", "title": "Apple 10-K", "filingDate": "2025-09-27", "file": "missing.html" } ] }""");

        Assert.Contains("missing.html", Assert.Throws<InvalidOperationException>(() => DemoManifest.Load(_folder)).Message);
    }

    [Fact]
    public void An_empty_manifest_is_an_error()
    {
        WriteManifest("""{ "filings": [] }""");

        Assert.Throws<InvalidOperationException>(() => DemoManifest.Load(_folder));
    }

    [Fact]
    public void The_committed_manifest_lists_the_four_filings_and_every_file_exists()
    {
        var repoSamples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/edgar"));

        var filings = DemoManifest.Load(repoSamples);

        Assert.Equal(["Apple 10-K", "Apple 10-Q", "Morgan 10-K", "Morgan 10-Q"], filings.Select(f => f.Title));
    }
}
```

Check the `../` depth against the unit test output path `tests/Lumen.UnitTests/bin/Debug/net10.0/`, which is five levels to the repo root.

- [ ] **Step 3: Failing integration tests** (`DemoFilingSeederTests`). The Apple 10-K fixture is `Fixtures/apple-10k-fy2025.htm` in the integration test output.

```csharp
namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class DemoFilingSeederTests(PostgresFixture fixture) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lumen-demo-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private async Task<string> PrepareAsync(string tenantName)
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "apple-10k-fy2025.htm"), Path.Combine(_folder, "aapl.html"));
        await File.WriteAllTextAsync(Path.Combine(_folder, "manifest.json"),
            $$"""{ "filings": [ { "tenant": "{{tenantName}}", "title": "Seeded Apple 10-K", "filingDate": "2025-09-27", "file": "aapl.html" } ] }""");
        return _folder;
    }

    private WebApplicationFactory<Program> FactoryWith(string folder) =>
        TestWebApplicationFactory.Create(fixture.ConnectionString).WithWebHostBuilder(b => b.UseSetting("Demo:FilingsPath", folder));

    [Fact]
    public async Task Startup_ingests_the_manifest_into_its_tenant_once()
    {
        var tenantName = $"Demo {Guid.NewGuid():N}";
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, tenantName, Role.Admin);
        var (_, otherEmail, otherPassword) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, $"Other {Guid.NewGuid():N}", Role.Admin);
        var folder = await PrepareAsync(tenantName);

        using (var first = FactoryWith(folder))
        {
            using var client = first.CreateClient();
            await TestAuth.AuthenticateAsync(client, email, password);
            var documents = await client.GetFromJsonAsync<List<DocumentDto>>("/api/documents");
            var seeded = Assert.Single(documents!, d => d.Title == "Seeded Apple 10-K");
            Assert.False(string.IsNullOrEmpty(seeded.IngestProfile));

            using var other = first.CreateClient();
            await TestAuth.AuthenticateAsync(other, otherEmail, otherPassword);
            Assert.DoesNotContain(await other.GetFromJsonAsync<List<DocumentDto>>("/api/documents") ?? [], d => d.Title == "Seeded Apple 10-K");
        }

        using var second = FactoryWith(folder);
        using var again = second.CreateClient();
        await TestAuth.AuthenticateAsync(again, email, password);
        Assert.Single(await again.GetFromJsonAsync<List<DocumentDto>>("/api/documents") ?? [], d => d.Title == "Seeded Apple 10-K");
    }

    [Fact]
    public async Task An_unknown_tenant_stops_startup_and_names_it()
    {
        var folder = await PrepareAsync("No Such Tenant Corp");

        using var factory = FactoryWith(folder);
        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("No Such Tenant Corp", error!.ToString());
    }
}
```

`DocumentDto` exposes `IngestProfile`. Check its property name and use it as it is.

- [ ] **Step 4: Run to verify failure** (compile errors, then the seeding doesn't happen).

- [ ] **Step 5: Implement.** `DemoFiling.cs`:

```csharp
namespace Lumen.Api.Seeding;

public record DemoFiling(string Tenant, string Title, DateOnly FilingDate, string File);
```

`DemoManifest.cs`:

```csharp
using System.Text.Json;

namespace Lumen.Api.Seeding;

public static class DemoManifest
{
    public const string FileName = "manifest.json";

    private sealed record ManifestFile(List<DemoFiling>? Filings);

    public static IReadOnlyList<DemoFiling> Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Demo:FilingsPath '{folder}' has no {FileName}.");
        }

        var filings = JsonSerializer.Deserialize<ManifestFile>(File.ReadAllText(path), JsonSerializerOptions.Web)?.Filings ?? [];
        if (filings.Count == 0)
        {
            throw new InvalidOperationException($"{path} lists no filings.");
        }

        foreach (var filing in filings)
        {
            if (string.IsNullOrWhiteSpace(filing.Tenant) || string.IsNullOrWhiteSpace(filing.Title) || string.IsNullOrWhiteSpace(filing.File))
            {
                throw new InvalidOperationException($"{path}: every filing needs tenant, title and file.");
            }

            if (!File.Exists(Path.Combine(folder, filing.File)))
            {
                throw new InvalidOperationException($"{path}: file '{filing.File}' for '{filing.Title}' does not exist.");
            }
        }

        return filings;
    }
}
```

`DemoFilingSeeder.cs`:

```csharp
using System.Diagnostics;

namespace Lumen.Api.Seeding;

public class DemoFilingSeeder
{
    private readonly AppDbContext _db;
    private readonly CurrentTenant _currentTenant;
    private readonly IMediator _mediator;
    private readonly ILogger<DemoFilingSeeder> _logger;

    public DemoFilingSeeder(AppDbContext db, CurrentTenant currentTenant, IMediator mediator, ILogger<DemoFilingSeeder> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task SeedAsync(string folder, CancellationToken ct)
    {
        var filings = DemoManifest.Load(folder);
        var seeded = 0;

        foreach (var filing in filings)
        {
            var tenant = await _db.Tenants.SingleOrDefaultAsync(t => t.Name == filing.Tenant, ct)
                ?? throw new InvalidOperationException($"Demo filing '{filing.Title}': tenant '{filing.Tenant}' does not exist (enable Database:SeedDemoData or create it first).");

            await TenantScopedWriter.RunAsync(_currentTenant, tenant.Id, async () =>
            {
                if (await _db.Documents.AnyAsync(d => d.Title == filing.Title, ct))
                {
                    _logger.LogInformation("Demo filing {Title} already exists for {Tenant}; skipped.", filing.Title, filing.Tenant);
                    return;
                }

                var stopwatch = Stopwatch.StartNew();
                await using var stream = File.OpenRead(Path.Combine(folder, filing.File));
                var result = await _mediator.Send(new UploadDocumentCommand(filing.File, stream, filing.Title, filing.FilingDate), ct);
                seeded++;
                _logger.LogInformation("Seeded demo filing {Title} for {Tenant}: {Chunks} chunks in {Elapsed} ms.",
                    filing.Title, filing.Tenant, result.ChunkCount, stopwatch.ElapsedMilliseconds);
            });
        }

        _logger.LogInformation("Demo filings: {Seeded} of {Total} ingested, the rest already present.", seeded, filings.Count);
    }
}
```

- Register in `AddWebApi()`: `builder.Services.AddScoped<DemoFilingSeeder>();`.
- `Tenants` is not tenant-filtered (only Users/Documents/Chunks have query filters), so the lookup by name works.
- Check that `AppDbContext`, `CurrentTenant` (`Lumen.Infrastructure.Tenancy`), `UploadDocumentCommand` and `IMediator` are already global in Api. Add missing namespaces per the GlobalUsings rule.

Add the third step to `UseStartupDataAsync`, after seeding:

```csharp
        var demo = app.Services.GetRequiredService<IOptions<DemoOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(demo.FilingsPath))
        {
            var folder = Path.IsPathRooted(demo.FilingsPath) ? demo.FilingsPath : Path.Combine(app.Environment.ContentRootPath, demo.FilingsPath);
            await scope.ServiceProvider.GetRequiredService<DemoFilingSeeder>().SeedAsync(folder, CancellationToken.None);
        }
```

- [ ] **Step 6: Run.** Unit suite, then the full integration suite: green. `DemoFilingSeederTests` ingests the real Apple 10-K with Fake embeddings, a few seconds.

- [ ] **Step 7: Stage** (including `samples/edgar/manifest.json`; the four `.html` files are staged by the user's choice, so stage them too with `git add samples/edgar`).

---

### Task 5: Container — NLog switch, Dockerfile, compose, `.env`, smoke script

**Files:**
- Modify: `src/Lumen.Api/nlog.config`, `src/Lumen.Api/appsettings.json`, `docker-compose.yml`, `.gitignore`
- Create: `Dockerfile`, `.dockerignore`, `.env.example`, `tools/compose-smoke.sh`

**Interfaces:**
- Consumes: the flags from Tasks 1–4, the health paths from Task 3, `Logs:FileMinLevel` (correction 1).

- [ ] **Step 1: NLog.** Split the rule in `nlog.config`:

```xml
  <rules>
    <logger name="Microsoft.*" maxlevel="Info" final="true" />
    <logger name="*" minlevel="Info" writeTo="console" />
    <logger name="*" minlevel="${configsetting:item=Logs.FileMinLevel:whenEmpty=Info}" writeTo="file" />
  </rules>
```

Add `"Logs": { "FileMinLevel": "Info" }` to `appsettings.json`.

Verify by hand (Claude): `Logs__FileMinLevel=Off dotnet run --project src/Lumen.Api` writes console output and creates no new file under `src/Lumen.Api/bin/Debug/net10.0/Logs/` (check the directory listing before and after). With the variable unset, today's file appears. Record both observations in the task report.

If `${configsetting}` doesn't resolve with `UseNLog()`, fall back to `minlevel="${environment:LUMEN_FILE_LOG_LEVEL:whenEmpty=Info}"` and report the deviation.

- [ ] **Step 2: Dockerfile** (repo root):

```dockerfile
# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG TARGETARCH
WORKDIR /src

COPY src/Lumen.Domain/Lumen.Domain.csproj src/Lumen.Domain/
COPY src/Lumen.Application/Lumen.Application.csproj src/Lumen.Application/
COPY src/Lumen.Infrastructure/Lumen.Infrastructure.csproj src/Lumen.Infrastructure/
COPY src/Lumen.Api/Lumen.Api.csproj src/Lumen.Api/
RUN dotnet restore src/Lumen.Api/Lumen.Api.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/Lumen.Api/Lumen.Api.csproj -c Release -a $TARGETARCH --no-restore -o /app \
    && mkdir -p /out/data/documents

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
COPY --from=build --chown=$APP_UID:$APP_UID /out/data /data
EXPOSE 8080
ENTRYPOINT ["./Lumen.Api"]
```

`.dockerignore`:

```
**/bin/
**/obj/
.git
.research/
.superpowers/
**/App_Data/
**/Logs/
tests/
tools/
samples/
.env
.vs/
.vscode/
.idea/
**/appsettings.Local.json
Dockerfile*
**/*.md
```

- [ ] **Step 3: compose.** Replace `docker-compose.yml`:

```yaml
services:
  postgres:
    image: pgvector/pgvector:pg17
    environment:
      POSTGRES_DB: lumen
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-postgres}
    ports:
      - "${POSTGRES_PORT:-5432}:5432"
    volumes:
      - lumen-postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER} -d $${POSTGRES_DB}"]
      interval: 5s
      timeout: 3s
      retries: 20

  api:
    build: .
    depends_on:
      postgres:
        condition: service_healthy
        restart: true
    restart: unless-stopped
    ports:
      - "${API_PORT:-5119}:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Default: Host=postgres;Port=5432;Database=lumen;Username=postgres;Password=${POSTGRES_PASSWORD:-postgres}
      Jwt__Secret: ${JWT_SECRET:?set JWT_SECRET in .env (at least 32 characters)}
      AllowedHosts: localhost;127.0.0.1
      Mcp__AllowedOrigins__0: ${MCP_ALLOWED_ORIGIN:-http://localhost:6274}
      Embeddings__Provider: ${EMBEDDINGS_PROVIDER:-Fake}
      Embeddings__Endpoint: ${EMBEDDINGS_ENDPOINT:-}
      Embeddings__Deployment: ${EMBEDDINGS_DEPLOYMENT:-}
      Embeddings__ApiKey: ${EMBEDDINGS_API_KEY:-}
      Chat__Provider: ${CHAT_PROVIDER:-Fake}
      Chat__Endpoint: ${CHAT_ENDPOINT:-}
      Chat__Deployment: ${CHAT_DEPLOYMENT:-}
      Chat__ApiKey: ${CHAT_API_KEY:-}
      Database__MigrateOnStartup: "true"
      Database__SeedDemoData: "true"
      Demo__FilingsPath: /seed
      DocumentStorage__RootPath: /data/documents
      Swagger__Enabled: "true"
      Logs__FileMinLevel: "Off"
    volumes:
      - lumen-documents:/data
      - ./samples/edgar:/seed:ro

volumes:
  lumen-postgres-data:
  lumen-documents:
```

`.env.example`:

```
# Copy to .env and fill in. .env is gitignored.
JWT_SECRET=change-me-to-a-random-string-of-at-least-32-chars
POSTGRES_PASSWORD=postgres

# Fake by default (no network). To use Azure OpenAI set the provider to AzureOpenAI and fill the rest.
# Switching the embeddings provider requires `docker compose down -v` (vectors from two models must not share a table).
EMBEDDINGS_PROVIDER=Fake
EMBEDDINGS_ENDPOINT=
EMBEDDINGS_DEPLOYMENT=
EMBEDDINGS_API_KEY=
CHAT_PROVIDER=Fake
CHAT_ENDPOINT=
CHAT_DEPLOYMENT=
CHAT_API_KEY=

MCP_ALLOWED_ORIGIN=http://localhost:6274
```

`.gitignore`: add `.env`. Keep `.env.example` tracked.

- [ ] **Step 4: `tools/compose-smoke.sh`** (bash 3.2-compatible: macOS has no associative arrays; `python3` for JSON). The script:

```bash
#!/usr/bin/env bash
# End-to-end check of `docker compose up`: health, demo data, search/ask, Swagger, MCP, restart and volume persistence.
set -euo pipefail
cd "$(dirname "$0")/.."
# Own project name and ports: the developer's compose stack (same volume names under project "dotnet-mcp-rag-server")
# may hold a corpus embedded with Azure; the smoke run must start from empty volumes and never touch it.
export COMPOSE_PROJECT_NAME=lumen-smoke API_PORT=55119 POSTGRES_PORT=55432
API=http://localhost:$API_PORT
created_env=no
if [ ! -f .env ]; then cp .env.example .env; created_env=yes; fi
trap 'docker compose down -v >/dev/null 2>&1 || true; [ "$created_env" = yes ] && rm -f .env' EXIT

fail() { echo "FAIL: $*" >&2; exit 1; }
wait_ready() { for _ in $(seq 1 180); do [ "$(curl -s -o /dev/null -w '%{http_code}' $API/health/ready)" = 200 ] && return 0; sleep 1; done; fail "health/ready never became 200"; }
token() { curl -sf -X POST $API/api/auth/token -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"Passw0rd!\"}" | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])'; }

docker compose up -d --build
wait_ready
[ "$(curl -s -o /dev/null -w '%{http_code}' $API/health/live)" = 200 ] || fail "health/live"
[ "$(curl -s -o /dev/null -w '%{http_code}' $API/swagger/index.html)" = 200 ] || fail "swagger"

for pair in "admin@acme.test:Apple 10-K,Apple 10-Q" "admin@globex.test:Morgan 10-K,Morgan 10-Q"; do
  email=${pair%%:*}; expected=${pair#*:}
  T=$(token "$email")
  curl -sf $API/api/documents -H "Authorization: Bearer $T" | python3 -c '
import sys,json
docs=json.load(sys.stdin); want=sys.argv[1].split(",")
titles=sorted(d["title"] for d in docs)
assert titles==sorted(want), titles
assert all(d.get("ingestProfile") for d in docs), "missing ingest profile"
print("documents ok:", titles)' "$expected"
  curl -sf -X POST $API/api/search -H "Authorization: Bearer $T" -H 'Content-Type: application/json' -d '{"query":"total net sales","topK":3}' >/dev/null || fail "search $email"
  curl -sf -X POST $API/api/ask -H "Authorization: Bearer $T" -H 'Content-Type: application/json' -d '{"question":"What were total net sales?"}' >/dev/null || fail "ask $email"
  curl -sf -X POST $API/mcp -H "Authorization: Bearer $T" -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
    -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | grep -q search_filings || fail "mcp tools/list $email"
done

docker compose restart api
wait_ready
docker compose logs api | grep -q "already exists" || fail "seeding was not skipped on restart"

docker compose down
docker compose up -d
wait_ready
volume=lumen-smoke_lumen-documents
docker run --rm -v "$volume":/data alpine ls -lnR /data/documents | tee /dev/stderr | grep -q ' 1654 ' || fail "raw files missing or not owned by UID 1654"

echo "SMOKE OK"
```

If the MCP stateless endpoint needs an extra header (e.g. `MCP-Protocol-Version`) to answer a bare `tools/list`, add it and note why. Check against `tests/Lumen.IntegrationTests/Mcp` or the Stage 5 notes. `chmod +x` the script.

- [ ] **Step 5: Verify.** Run these yourself (Docker is available):
  - `docker compose config` with a `.env` that lacks `JWT_SECRET` → error mentioning `JWT_SECRET` (Review Focus 5);
  - `bash tools/compose-smoke.sh` → `SMOKE OK`;
  - `docker buildx build --platform linux/amd64,linux/arm64 -t lumen-api:multiarch .` builds both (no push; if the builder lacks multi-platform support, `docker buildx create --use` first and report it).

  Record image size (`docker image ls`), cold-start time to `/health/ready`, and the seed time per filing from the logs in the report. These are numbers for DECISIONS.md.

  The smoke script runs as compose project `lumen-smoke` on ports 55119/55432 and removes its own volumes on exit (`down -v` in the trap), so the developer's stack (project `dotnet-mcp-rag-server`, volume `lumen-postgres-data` with the Azure-embedded corpus) is never touched. Do NOT run `docker compose down -v` on the default project.

- [ ] **Step 6: Stage** (Dockerfile, .dockerignore, compose, .env.example, .gitignore, nlog.config, appsettings.json, script).

---

### Task 6: Docs

**Files:** `README.md`, `CLAUDE.md`

- [ ] **Step 1: README.**
  - In "Getting started", add a "Run with Docker" subsection first (note: if the local Postgres volume already holds a corpus from `dotnet run` with Azure embeddings, start the demo clean with `docker compose down -v` first — seeding skips titles that exist): `cp .env.example .env`, set `JWT_SECRET`, `docker compose up --build`; URLs (`http://localhost:5119/swagger`, `/health/ready`, `/mcp`); demo logins `admin@acme.test` / `admin@globex.test`, `Passw0rd!`; what gets seeded (four filings, Fake providers); switching to Azure via `.env`, with `docker compose down -v` when changing the embeddings provider; `tools/compose-smoke.sh`.
  - Keep the existing `dotnet run` instructions as "Run from source". They now need `docker compose up -d postgres` only.
  - Add a "How I'd deploy to Azure" section using design §8 and research §2.9, with the numbers: pgvector 0.8.2 on Flexible Server, App Service health-check behaviour (1-minute ping, removed after 10 failures), `WEBSITES_PORT=8080`.
  - Update the Roadmap line for Stage 6.
- [ ] **Step 2: CLAUDE.md.**
  - Commands: the compose flow, the smoke script, the flags (`Database:*`, `Demo:FilingsPath`, `Swagger:Enabled`, `Logs:FileMinLevel`).
  - A status paragraph "Stage 6A (run anywhere) implemented" with the test counts and the smoke-run numbers from Task 5.
  - Note that `samples/edgar/` now holds the four filings (moved from `.research`).
- [ ] **Step 3: Final verification.** Run:
  - `dotnet build` (0 warnings);
  - all three test projects (`DOCKER_API_VERSION=1.43` for integration);
  - `grep -rn IgnoreQueryFilters src`;
  - `git diff --cached --stat` against the File map.
- [ ] **Step 4: Stage.**

---

## After implementation (Claude)

- `DECISIONS.md`: entries only with numbers from the smoke run, e.g. "chiseled-extra image (X MB) over full aspnet (Y MB)" and "runtime migrate + seeding at startup (cold start Z s, Npgsql table lock) over a migration bundle".
- Roadmap and `.research/README.md` updated; Stage 6 closed.
