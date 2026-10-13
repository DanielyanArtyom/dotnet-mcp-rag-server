# Lumen — Stage 6A design spec: run anywhere with `docker compose up`

Status: **draft. The design was approved in conversation on 2026-10-12; this written spec is awaiting user review.**

Inputs:
- the kickoff (`../discussion/stage-6-kickoff.md`, row 1: the Azure deploy is skipped and Stage 6 becomes "Run anywhere");
- the research note `run-anywhere-containers.md` (cited as **R§n**);
- the user's decisions of 2026-10-12:
  - the demo data seeds the four real filings;
  - the filings move to `samples/edgar/` (done; `.research/stage-2/samples/` deleted);
  - the placeholder "Welcome" documents go;
  - Swagger is on in compose.

Feeds `writing-plans` → `plan-6a.md`.

## Goal and success criterion

A fresh clone plus `cp .env.example .env` (and filling in `JWT_SECRET`), then `docker compose up --build`, gives a working Lumen on `http://localhost:5119`:
- Postgres with pgvector, the schema migrated, the two demo tenants and their admins;
- the four EDGAR filings ingested through the normal pipeline, with Fake embeddings and chat by default and Azure when `.env` says so;
- `/health/live` and `/health/ready` answering;
- Swagger at `/swagger`;
- `/api/ask`, `/api/search` and `/mcp` working with the existing `lumen-token.sh`, and the eval harness unchanged.

No dev values reach Production. A missing secret stops startup with a message that names the setting. `tools/compose-smoke.sh` proves all of this end to end (Claude runs it).

**Out of scope:**
- the actual Azure deploy (only a README section, "How I'd deploy to Azure");
- the two-replica nginx test (deferred);
- a container `HEALTHCHECK` for the API itself;
- migration bundles;
- renaming the "Morgan" titles (kept for comparability with the Stage 6B runs).

## Current state, and what breaks in a container (R§1)

1. Migrate and seed run only in Development (`ApplicationBuilderExtension.cs:7-10`).
2. The connection string and JWT secret live only in `appsettings.Development.json`.
3. `JwtOptions` has no validation. A missing secret throws a nameless `ArgumentNullException`, and a short secret fails at the first login.
4. NLog writes a file target under `${basedir}/Logs`, and uploads go to `App_Data/Documents`. Neither is writable by the chiseled image's non-root user (UID 1654).
5. The container port is 8080, while all local tooling uses 5119.
6. There is no `/health`, and Swagger is Development-only.
7. `DbSeeder` adds two "Welcome" documents with no file, no chunks and no ingest profile.
8. There is no Dockerfile and no `.dockerignore`; compose runs Postgres only.

## Design

### 1. Image (R§2.1)

- **Multi-stage `Dockerfile` at the repo root.**
  - Build stage: `FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble`, `ARG TARGETARCH`. Copy the four `src/*/…csproj` files first, then `dotnet restore -a $TARGETARCH src/Lumen.Api/Lumen.Api.csproj` as its own cached layer, then the sources, then `dotnet publish src/Lumen.Api -c Release -a $TARGETARCH --no-restore -o /app`.
  - Create `/out/data/documents` in this stage.
  - Final stage: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`, `COPY --from=build /app .`, `COPY --from=build --chown=$APP_UID:$APP_UID /out/data /data`, `EXPOSE 8080`, exec-form `ENTRYPOINT ["./Lumen.Api"]`.
  - The process runs as the image's non-root user. arm64 and amd64 both build natively, without emulation.
- **Why `-extra` (ICU + tzdata, about 68 MB) and not plain chiseled (about 53 MB, invariant globalization):** R§2.1 found nothing culture-sensitive in Lumen's own code, but invariant mode inside SemanticKernel's `TextChunker`, AngleSharp and PdfPig is unverified. 15 MB is cheaper than a chunk-count regression. This is a possible later optimization (open question).
- **`.dockerignore`:** `**/bin`, `**/obj`, `.git`, `.research`, `.superpowers`, `**/App_Data`, `**/Logs`, `tests`, `tools`, `samples`, `.env`, IDE folders, `**/appsettings.Local.json`, `Dockerfile*`, `**/*.md`. `samples/` stays out of the image because compose mounts it.

### 2. Health (R§2.2, R§2.3)

- `AddHealthChecks().AddDbContextCheck<AppDbContext>(tags: ["ready"])`, from `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`, the version matching EF Core (10.0.12).
- `/health/live` uses `Predicate = _ => false`, so it is always Healthy while the process runs. `/health/ready` runs the `ready` checks: 200 when the database answers, 503 otherwise.
- Both endpoints are anonymous: Lumen has no fallback policy and they get no `RequireAuthorization`. They use the default plaintext writer.
- They are mapped next to the controllers. `TenantResolutionMiddleware` passes requests without the claim through; an integration test pins this.
- Compose: `pg_isready` healthcheck on Postgres; the API uses `depends_on: postgres: condition: service_healthy` and `restart: unless-stopped`. The API itself has no container healthcheck, because the chiseled image has no shell or curl (dotnet-docker #4300 is open). API health is checked from the host and by the smoke script.

### 3. Startup steps behind flags (R§2.4)

- New `DatabaseOptions` (section `Database`, in `Lumen.Api/DependencyInjection` next to the other Api options) with `MigrateOnStartup` and `SeedDemoData`, both bool and default `false`.
- New `DemoOptions` (section `Demo`) with `FilingsPath` (string, default empty).
- `appsettings.Development.json` sets `Database:MigrateOnStartup = true` and `Database:SeedDemoData = true`. This keeps today's dev and integration-test behaviour; the test host runs as Development. `Demo:FilingsPath` stays empty in Development, so filings are opt-in there.
- `UseDatabaseMigrationAsync` becomes `UseStartupDataAsync` and runs three steps in order, each behind its flag:
  1. **`MigrateOnStartup` → `Database.MigrateAsync()`.** On EF 9+ with Npgsql, `MigrateAsync` takes `LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE` for the transaction, so two replicas migrating at once are serialized (R§2.4). A migration bundle is the production path and is described in the Azure text.
  2. **`SeedDemoData` → `DbSeeder.SeedAsync`.** It seeds the tenants "Acme Corp" and "Globex Inc" and one Admin each (`admin@acme.test`, `admin@globex.test`, `Passw0rd!`). **No documents any more**: the "Welcome" documents are removed and `DbSeederTests` is updated.
  3. **`FilingsPath` non-empty → `DemoFilingSeeder`** (new, `Lumen.Api/Seeding/`). It reads `{FilingsPath}/manifest.json`. For each entry it resolves the tenant by name, sets the ambient tenant with `TenantScopedWriter`, and sends the existing `UploadDocumentCommand` through MediatR, so a seeded filing is identical to an uploaded one (chunks, embeddings, ingest profile, raw file).
     - It is idempotent: an entry whose tenant already has a document with that title is skipped.
     - It logs one Information line per filing (title, chunk count, elapsed time) and one summary line.
     - A missing manifest, a missing file or an unknown tenant fails startup with a clear message. Reason: a half-seeded demo is worse than a stopped one.
     - Embedding cost with Azure on: about 4.6k chunks once; skipped on later starts.
- **`samples/edgar/manifest.json`** (new, committed). Titles and dates match the Stage 6B corpus:

  ```json
  { "filings": [
    { "tenant": "Acme Corp",  "title": "Apple 10-K",  "filingDate": "2025-09-27", "file": "aapl-20250927.html" },
    { "tenant": "Acme Corp",  "title": "Apple 10-Q",  "filingDate": "2026-06-27", "file": "aapl-20260627.html" },
    { "tenant": "Globex Inc", "title": "Morgan 10-K", "filingDate": "2025-12-31", "file": "jpm-20251231.html" },
    { "tenant": "Globex Inc", "title": "Morgan 10-Q", "filingDate": "2026-06-30", "file": "jpm-20260630.html" }
  ] }
  ```
- **Integration test host:** pins `Demo:FilingsPath` to empty (on top of the existing pins). `DemoFilingSeeder` gets its own tests (§7).

### 4. Configuration and secrets (R§2.5)

- **Base `appsettings.json`** keeps safe non-secret defaults: Fake providers, no secret, no connection string, `AllowedHosts: "*"`. Compose and Azure override it; local `dotnet run` keeps working.
- **`JwtOptions` validation:** `AddOptions<JwtOptions>().Bind(...)` with an `IValidateOptions` validator (`Jwt:Secret` must be at least 32 bytes of UTF-8; `Issuer`/`Audience` non-empty) and `ValidateOnStart`, matching the other options. `AddAuthentication` currently reads the secret eagerly. It must either build the signing key from validated options (a `JwtBearerOptions` configure step that takes `IOptions<JwtOptions>`) or check explicitly and throw `InvalidOperationException("Jwt:Secret is not set …")`. The plan picks after reading the code; the requirement is that a missing or short secret stops startup with a message naming `Jwt:Secret`.
- **Missing `ConnectionStrings:Default`** also stops startup with a message naming it.
- **`Swagger:Enabled`** (bool; default: on in Development, off otherwise) replaces the `IsDevelopment()` check around `UseSwagger`/`UseSwaggerUI`. Compose sets it to `true`.
- **Compose `.env`** (gitignored) and a committed **`.env.example`**:
  - `JWT_SECRET`: required. Compose uses `${JWT_SECRET:?…}`, so a missing value stops `docker compose up` with a message.
  - `POSTGRES_PASSWORD`: default `postgres`.
  - `EMBEDDINGS_PROVIDER`, `EMBEDDINGS_ENDPOINT`, `EMBEDDINGS_DEPLOYMENT`, `EMBEDDINGS_API_KEY`: default Fake.
  - `CHAT_PROVIDER`, `CHAT_ENDPOINT`, `CHAT_DEPLOYMENT`, `CHAT_API_KEY`: default Fake.
  - `MCP_ALLOWED_ORIGIN`: default `http://localhost:6274`.
  - Switching embeddings provider means `docker compose down -v`, the same rule as today, because vectors from two models must not share the table. `.env.example` says so.
- **API service `environment:`** sets:
  - `ASPNETCORE_ENVIRONMENT=Production`;
  - `ConnectionStrings__Default=Host=postgres;Port=5432;Database=lumen;Username=postgres;Password=${POSTGRES_PASSWORD:-postgres}`;
  - `Jwt__Secret=${JWT_SECRET:?…}`;
  - `AllowedHosts=localhost;127.0.0.1`;
  - `Mcp__AllowedOrigins__0=${MCP_ALLOWED_ORIGIN:-http://localhost:6274}`;
  - the provider variables;
  - `Database__MigrateOnStartup=true`, `Database__SeedDemoData=true`;
  - `Demo__FilingsPath=/seed`;
  - `DocumentStorage__RootPath=/data/documents`;
  - `Swagger__Enabled=true`;
  - `Logging__File__Enabled=false` (§5).
- **Ports:** API `5119:8080`. Postgres `5432:5432` stays published so `dotnet run` and the tests can still use the same container, as today.

### 5. Logging (R§2.7)

- The `nlog.config` file rule is gated by a config value: the rule's `minlevel` comes from `${configsetting:item=Logging.File.MinLevel:whenEmpty=Info}`, or an equivalent the plan verifies.
- Compose sets it to `Off`, so the container logs to stdout only and the read-only `/app/Logs` is never touched. `dotnet run` keeps today's file logs.
- The plan verifies the exact NLog mechanism (`configsetting` with `UseNLog()`) with a startup test. If it doesn't work, it falls back to `${environment:…}`.

### 6. Storage (R§2.8)

- Named volume `lumen-documents` mounted at `/data`. The directory is pre-created with `--chown=$APP_UID`, so the first mount copies the ownership.
- `samples/edgar` is bind-mounted read-only at `/seed`.
- Postgres keeps its existing named volume.
- The smoke script checks that an upload persists across `docker compose down && up`, without `-v`.

### 7. Testing and verification

**Unit tests:**
- `JwtOptionsValidator` (missing / short / ok);
- `DatabaseOptions` and `Swagger:Enabled` defaults;
- manifest parsing (valid; missing file → clear error; unknown tenant → clear error).

**Integration tests (Testcontainers):**
- `/health/live` → 200 and `/health/ready` → 200 anonymously; `/health/ready` → 503 when the DB is unreachable (a factory with a bad connection string, if this can be done reliably; otherwise unit-test the check registration);
- `DbSeederTests` updated: users and tenants, no documents;
- **`DemoFilingSeeder`**, with a temp folder holding a manifest and the Apple 10-K fixture under the test host's Fake embeddings:
  - a first start creates the document with chunks and the current ingest profile in the right tenant, and the other tenant can't see it;
  - a second run is a no-op (same document count).
- Startup fails with a message naming `Jwt:Secret` when it is missing (a `WebApplicationFactory` with an empty secret, asserting the exception message).
- Existing tests stay green, and the tenant-isolation tests are untouched.

**`tools/compose-smoke.sh`** (Claude runs it). It:
1. writes a temporary `.env` if none exists;
2. runs `docker compose up -d --build`;
3. waits for `/health/ready`;
4. checks `/health/live`;
5. logs in as both admins;
6. checks that `GET /api/documents` lists the two filings of each tenant, each with a profile;
7. runs `/api/search` and `/api/ask` (Fake answers);
8. checks that `/swagger/index.html` → 200;
9. checks MCP `tools/list` with a token;
10. restarts with `docker compose restart api` and checks the documents are still there and the seeding was skipped (log line);
11. runs `docker compose down` (keeping volumes), then `up`, and checks the raw file still downloads or deletes.

It also checks both architectures: `docker buildx build --platform linux/amd64,linux/arm64` builds (no push).

### 8. Documentation

- README:
  - a "Run with Docker" section (`.env`, `docker compose up --build`, URLs, demo logins, switching to Azure, `down -v` when switching embeddings provider);
  - a short "How I'd deploy to Azure" section from R§2.9: App Service Linux container with `WEBSITES_PORT=8080`, health check path `/health/ready`, managed identity for Azure OpenAI through the existing empty-`ApiKey` path, Flexible Server with `vector` in `azure.extensions` (pgvector 0.8.2), Key Vault references for `Jwt__Secret` and the connection string, Blob Storage behind `IFileStorage`, and a migration bundle as a deploy step.
- CLAUDE.md: commands and a status line.
- DECISIONS.md: only decisions with a number, e.g. image size, or "runtime migrate under the Npgsql lock vs bundle" with the measured startup or seed time.

## Open questions (not blocking)

- Plain chiseled plus `InvariantGlobalization` (−15 MB) after one characterization run in invariant mode: a possible Stage 7 optimization.
- Whether named-volume ownership copying works on the installed Docker version. The smoke script verifies it; the fallback is the volume under `/home/app`.
- An API container healthcheck (`dotnet Lumen.Api.dll healthcheck` subcommand): only when the deferred two-replica test needs it.
