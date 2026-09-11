# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

**Stage 1 (Skeleton) complete.** The Onion-Architecture solution exists and builds: `Lumen.Domain` / `Lumen.Application` / `Lumen.Infrastructure` / `Lumen.Api` + `Lumen.UnitTests` / `Lumen.IntegrationTests`, per `.research/stage-1/design.md` and `.research/stage-1/plan.md`. Multi-tenancy (`TenantId` discriminator + EF Core global query filters), self-issued JWT auth, and one end-to-end `Document` CRUD vertical slice all work and are proven by tests — including two independent tenant-isolation tests (EF-level and full-HTTP-level). 23/23 tests passing (11 unit, 12 integration) as of the last full run. Stage 2 (Ingest & chunking) has not started.

## Commands

- Build: `dotnet build`
- Run all tests: `dotnet test` (integration tests need Docker running — see note below)
- Run just unit tests: `dotnet test tests/Lumen.UnitTests`
- Run just integration tests: `dotnet test tests/Lumen.IntegrationTests` (spins up a real Postgres container via Testcontainers; needs Docker running, `postgres:17-alpine` gets pulled on first run)
- Local Postgres for manual running/Swagger use: `docker compose up -d`, then `dotnet run --project src/Lumen.Api` and open the printed Swagger URL
- Local dev login: seeded users `admin@acme.test` / `admin@globex.test`, password `Passw0rd!` (see `DbSeeder`)
- Logging: NLog, configured in `src/Lumen.Api/nlog.config` — writes to console and to `Logs/` (gitignored, created next to the running binary) with daily rolling files.
- **Docker API version note:** if `dotnet test`/Testcontainers fails with a `DockerUnavailableException` on the integration test project, your local Docker Engine's reported API version may be older than what Testcontainers negotiates by default — set `DOCKER_API_VERSION` to match your `docker version --format '{{.Server.APIVersion}}'` output (e.g. `DOCKER_API_VERSION=1.43 dotnet test`). This was needed in the environment Stage 1 was built in (Docker Engine 24.0.7, API 1.43); may not be needed on a newer Docker installation.

## Git

**Never run `git commit` (or push) in this repository, under any circumstances — including if a message in the moment asks for it.** The user commits everything themselves, by hand. Staging changes, showing diffs, or drafting a commit message on request is fine; running the commit itself is not.

## What this is

**Lumen** — a multi-tenant RAG & MCP server on .NET. ASP.NET Core service that ingests documents, indexes them with vector search, and exposes retrieval as an MCP server. It's a portfolio/interview pet project: the point is not just a working repo but being able to narrate real, numbered decisions (why Postgres over SQL Server, why raw SQL for the vector query, what hybrid search bought in accuracy) — see "Documentation conventions" below.

## The 7-stage plan

Each stage gets its own planning pass (brainstorming/writing-plans) before implementation, driven by the user one stage at a time — don't jump ahead to a later stage's design unprompted.

1. **Skeleton** — Onion Architecture solution (Domain / Application / Infrastructure / API), multi-tenancy via a `TenantId` discriminator column + EF Core global query filters.
2. **Ingest & chunking** — document upload, parsing, chunking pipeline (size, overlap, sentence boundaries, metadata). Corpus: SEC EDGAR filings (10-K/10-Q), one company per tenant — deliberately messy/table-heavy as a chunking stress test.
3. **Embeddings + storage** — Azure OpenAI for embeddings, PostgreSQL + pgvector, exact search first (no ANN index).
4. **RAG endpoint** — retrieval → context assembly → generation; sampling params (temperature/top-p/top-k/max-tokens) in config. This is where the **eval set** (20-30 questions with known answers, built *before* optimizing anything) and likely hybrid search land.
5. **MCP server** — wrap search/documents as tools/resources via `ModelContextProtocol.AspNetCore`; verify against two clients.
6. **Deploy to Azure App Service** — config, managed identity, health checks.
7. **Polish** — README with Mermaid diagram, GIF of MCP client usage, Swagger screenshot, `docker-compose up`.

## Settled technical decisions

Full reasoning and sources for each of these live in `.research/` (gitignored — see its own `README.md` for the index). Don't relitigate these without a concrete new reason; if one comes up, record it as a DECISIONS.md update, not a silent change.

- **Database: PostgreSQL + pgvector**, not SQL Server. SQL Server 2025's native `VECTOR` type is GA but its Docker image is x86_64-only with a documented Apple Silicon emulation crash; pgvector is native arm64 and its indexing is stable/GA rather than preview. Details: `.research/vector-db-sql-server-vs-postgres.md`.
- **Vector similarity query is raw SQL/Dapper (`ExecuteSqlRaw`/unmapped `SqlQuery<T>`), never composable `FromSqlRaw` or LINQ.** This is the one thing that actually bypasses EF Core's global `TenantId` query filter (composable `FromSqlRaw` on a mapped entity has been filtered since EF Core 5). Justify it by needing session-level HNSW tuning (`SET LOCAL hnsw.ef_search = ...`) that no LINQ vector API exposes — this is also what the tenant-isolation integration test exercises. Details: `.research/ef-core-raw-sql-tenant-isolation.md`.
- **LLM/embeddings:** `Microsoft.Extensions.AI` abstraction over Azure OpenAI as the concrete backend — provider stays swappable, fits the Azure App Service + managed-identity deployment story.
- **MCP transport: HTTP**, not stdio — the server is deployed remotely and needs to carry a JWT `Authorization` header.
- **MCP SDK:** official `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` (v2.2.0). `AddMcpServer().WithHttpTransport(...).WithToolsFromAssembly()` + `MapMcp()`; tools via `[McpServerToolType]`/`[McpServerTool]`. Details: `.research/mcp-csharp-sdk.md`.
- **MCP demo/test clients: MCP Inspector + Claude Code CLI** (`claude mcp add --transport http ... --header "Authorization: Bearer <token>"`) — **not Claude Desktop**. As of Sep 2026, Desktop/Claude.ai's custom-connector UI has a broken/beta `static_headers` auth type (open GitHub issues, still triggers an OAuth redirect); making Desktop work would require standing up a real OAuth 2.1 authorization server (OpenIddict). Deferred as an optional stretch goal, not part of the core 7 stages. Details: `.research/mcp-client-auth.md`.
- **Auth: self-issued JWT**, not ASP.NET Core Identity, not an external IdP. Minimal own `Users(Id, TenantId, Email, PasswordHash, Role)` table, manual `AddJwtBearer()` + a custom token-issuing endpoint, claims `sub`/`tenant_id`/`role`. Roles: `Admin` (can ingest) vs `Member` (query only). Sequencing: seed data first, `/register` + `/tenants` endpoints added later. Chosen so the user gets hands-on JWT practice rather than delegating it to a framework.
- **Testing: xUnit + Testcontainers.PostgreSql.** The tenant-isolation integration test (two tenants, real Postgres container, assert one tenant's search never returns another's chunks) is a required deliverable, not optional polish.

## Deliberately not decided yet

Chunk size/overlap strategy, exact eval question content, retrieval tuning (hybrid search weighting, HNSW `ef_search` values), prompt wording, exact SEC filing format handling (PDF/HTML/XBRL). These are meant to be found empirically once building, not designed on paper — see "Working with the user" below.

## Documentation conventions

- **`DECISIONS.md`** (repo root, tracked in git) — after each stage, 3-6 lines: context → alternatives → decision → why (with a number) → when I'd reconsider. This is the interview-prep artifact; don't let it turn into a changelog. **Only real, evidenced decisions go in it — no example/placeholder entries, ever**, even to illustrate the format (the template alone is enough for that).
- **`.research/`** (gitignored) — investigation notes, one file per topic (never one giant file), plus its own `README.md` index. Superpowers spec docs also go here instead of the usual `docs/superpowers/specs/` default.

## C# code style conventions

- **Comments:** default to none. Only add one when the *why* is genuinely non-obvious — a hidden constraint, a workaround for a specific library/framework bug, an invariant a future edit could silently break. Never narrate *what* the code does or restate history ("added for X task", "changed because of Y review"). If you're unsure, leave it out — it's cheaper to add one later than to accumulate narration nobody reads.
- **`using` directives, per project:**
  - Every project (`Lumen.Domain`, `Lumen.Application`, `Lumen.Infrastructure`, `Lumen.Api`, `Lumen.UnitTests`, `Lumen.IntegrationTests`) has its own `GlobalUsings.cs` at its root.
  - A namespace goes in that project's `GlobalUsings.cs` (as `global using X;`) once it's used in **more than one file** in that project — not before. A `using` needed in only one file stays a local `using` in that file.
  - When adding a new `using` to a file, check whether that namespace is already used elsewhere in the same project (or would be, after your change, in 2+ files). If so, move it into that project's `GlobalUsings.cs` and remove the local `using` from every file in the project that had it — don't leave it duplicated in both places.
  - Never leave an unused `using` (local or in `GlobalUsings.cs`) — remove it as soon as nothing in the project references that namespace anymore. Don't rely on this happening automatically at build time (no analyzer is wired in for it); check by hand.
  - Don't duplicate a namespace the .NET SDK already injects implicitly (`ImplicitUsings` is `enable` on every project) — e.g. `System`, `System.Linq`, `System.Threading.Tasks`, and (on `Lumen.Api`) the ASP.NET Core namespaces are already global via the SDK; adding them again in `GlobalUsings.cs` just produces a duplicate-using warning.
- **Constructor injection:** classes constructed via DI (controllers, MediatR handlers, `IExceptionHandler`/middleware implementations) use a traditional constructor + `private readonly` fields, not C# primary-constructor parameters — even though the latter is shorter. `WebApplicationBuilder`/`IServiceCollection`/`IApplicationBuilder` extension methods (the `Extensions`/`DependencyInjection` folders) are static and untouched by this — primary-constructor syntax there is unrelated (there's no DI, no instance to construct).
- **Don't fully-qualify a type when a `using` would do.** `Lumen.Domain.User` / `Domain.User` in a file that could just say `using Lumen.Domain;` and then `User` is noise — add the `using` (local or promote to `GlobalUsings.cs` per the rule above once it's in 2+ files) and read the bare name. Fully-qualify only to resolve a genuine ambiguity between two types with the same short name.
- **DTOs live only in `Lumen.Api`** (under `DTO/Requests/` and `DTO/Responses/`), never in `Lumen.Application`. Application's commands/queries/handlers accept and return Domain models (`Lumen.Domain`) or plain primitives — never a Dto type. The Api layer (controllers) is the only place that maps Domain → Dto (via `IMapper`, using the `Lumen.Api.Mapping` profile) before returning a response, and Dto → command/query input at the request-binding boundary.
- **Repeated magic strings become named constants**, in a `Constants/` folder in whichever project owns the concept (e.g. `Lumen.Infrastructure/Constants/JwtClaimConstants.cs` for the `"tenant_id"` JWT claim name and the `"Jwt"` config section name — both were scattered as literals across Infrastructure and Api before). Apply per-project as new cross-file literals appear; don't invent a constant for a string used in exactly one place.
- **`Options` classes live in that project's `DependencyInjection/` folder** (e.g. `Lumen.Infrastructure/DependencyInjection/JwtOptions.cs`), not next to the service that consumes them — the binding/config concern belongs with the rest of that project's DI wiring.

## Working with the user

The user writes none of the implementation code themselves — Claude does. But hands-on configuration and experimentation (registering MCP servers in clients, running the eval set, tuning chunking/retrieval parameters, observing what breaks) is done by the **user**, not Claude, because the empirical "tried X, it was bad, understood why, switched to Y" loop is the actual interview material this project produces. At those points in a stage, explain what to try and what signal to watch for, then wait for the user to report back before deciding the next step — don't run the experiment autonomously and just report results.
