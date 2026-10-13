# Lumen — Stage 1 design spec: Skeleton (Onion + multi-tenancy + auth)

Status: approved for implementation. Produced via brainstorming/grilling, 2026-09-09/10. Feeds `writing-plans` → `plan.md`.

> **Amendment (post-implementation, 2026-09-11):** this document is left as-written — it's the record of what was designed *before* building. Everything actually built matches it except the points below, all decided during a post-completion review pass, not during design. Full rationale: `.research/specification/csharp-project-conventions.md`; narrative: `.research/stage-1/progress.md`.
> - **Dto moved out of `Lumen.Application` into `Lumen.Api`** (§"Three classes per business concept" above described it living in Application). Application's MediatR handlers now pass Domain models straight through; the Api layer is the only place a Dto exists, and does the Domain↔Dto mapping itself. The "two AutoMapper profiles" split (Domain↔Entity in Infrastructure, Domain↔Dto in Api) still holds — only the second profile's project moved.
> - **Route is `api/[controller]`**, not `/auth/token`/`/documents` as bare paths — so `POST /api/auth/token`, `GET /api/documents`, etc.
> - **`AppDbContextFactory`/`IDesignTimeDbContextFactory<T>` was never actually needed** and was removed — not mentioned in the original design because its necessity was simply assumed; verified empirically that `dotnet ef` tooling builds the real `Program.cs` host on this .NET version.
> - **A real FK was added** `Documents.TenantId`/`Users.TenantId → Tenants.Id` (the design above didn't specify one either way).
> - **Tenant stamping in `AppDbContext.SaveChangesAsync` is unconditional**, not "only if `Guid.Empty`" — closes a gap where a client-supplied `TenantId` could otherwise have survived onto a new row.

## Scope

Stage 1 builds the foundation everything else sits on: solution structure, multi-tenancy mechanism, auth, and one real end-to-end vertical slice (`Document` CRUD) that proves the mechanism actually works — not just compiles. Explicitly **out of scope** for stage 1 (deferred to later stages, don't build now): `DocumentChunk`/embeddings (stage 2/3), PDF upload/parsing (stage 2), MCP server (stage 5), `/register` + `/tenants` self-service endpoints (later, once seed-based flow is proven), Azure deployment (stage 6), hybrid search (stage 4).

## Target framework

.NET 10 (current LTS, SDK installed 2026-09-10).

## Solution structure

```
Lumen.sln
src/
  Lumen.Domain           — domain models, zero external dependencies
  Lumen.Application      — MediatR Commands/Queries/Handlers, DTOs, repository interfaces, ICurrentTenant, AutoMapper profile (Domain <-> Dto)
  Lumen.Infrastructure    — EF Core (AppDbContext, *Entity classes, IEntityTypeConfiguration<T>), repositories + UnitOfWork, JWT issuing, AutoMapper profile (Domain <-> Entity)
  Lumen.Api               — Controllers, Swagger, Program.cs (composition root), middleware/handlers, docker-compose.yml
tests/
  Lumen.UnitTests         — Domain/Application logic, mocked repositories, no DB
  Lumen.IntegrationTests  — Testcontainers.PostgreSql, real Postgres
```

Dependency direction: `Domain` ← `Application` ← `Infrastructure` ← `Api` (arrows point toward what depends on what; `Domain` has no project references, `Api` is the only layer aware of ASP.NET Core/HTTP specifics).

## Three classes per business concept

Chosen deliberately (full separation, not DTO↔Entity shortcut):
- **Domain model** (`Lumen.Domain`) — e.g. `Document`. Persistence-ignorant, POCO, room for business logic later.
- **Entity** (`Lumen.Infrastructure`) — e.g. `DocumentEntity`. EF-mapped, configured via `IEntityTypeConfiguration<DocumentEntity>`, implements `ITenantEntity` where relevant.
- **Dto** (`Lumen.Application`) — e.g. `DocumentDto`. API-facing contract, used as MediatR command/query payload and response shape.

Two AutoMapper profiles: `Domain ↔ Entity` (Infrastructure, used inside repositories), `Domain ↔ Dto` (Application, used inside MediatR handlers). Both validated at startup (or in a unit test) via `configuration.AssertConfigurationIsValid()`.

## Entities for stage 1

- **`Tenant`** — `Id (Guid)`, `Name`. Root entity, not tenant-owned itself, no query filter.
- **`User`** — `Id (Guid)`, `TenantId (Guid)`, `Email`, `PasswordHash`, `Role (Admin | Member)`. Tenant-owned (`ITenantEntity`), but see the login exception below.
- **`Document`** — `Id (Guid)`, `TenantId (Guid)`, `Title`, `Content`. Tenant-owned. Minimal on purpose — stage 2 extends it (chunking metadata etc.), not replaces it.

`DocumentChunk` (with embedding column) is explicitly **not** part of stage 1 — it belongs to stage 2/3 once ingestion and embeddings exist.

## Multi-tenancy mechanism

- `ITenantEntity { Guid TenantId { get; set; } }` — marker interface, implemented by tenant-owned `*Entity` classes.
- `ICurrentTenant { Guid TenantId { get; } }` — interface in `Lumen.Application`. Implementation `CurrentTenant` (scoped service, settable) lives in `Lumen.Infrastructure`, populated by `TenantResolutionMiddleware` in `Lumen.Api` from the `tenant_id` claim on the authenticated JWT principal.
- `AppDbContext.OnModelCreating` applies `HasQueryFilter` to every `ITenantEntity` entity type, filtering on `ICurrentTenant.TenantId`.
- `AppDbContext.SaveChangesAsync` override auto-stamps `TenantId` on newly-added `ITenantEntity` entries from `ICurrentTenant`, so it's structurally impossible to forget setting it on create.
- **Registration:** plain `AddDbContext<AppDbContext>` (never `AddDbContextPool`/`AddPooledDbContextFactory`) — pooling reuses context instances across requests and can leak a captured tenant from a previous request into the query filter closure. Confirmed as a real risk pattern already present (for different, non-multi-tenant reasons) in a past project; deliberately avoided here.
- **Login exception:** `User` lookup during authentication happens *before* a tenant is known (you're finding out which tenant a user belongs to from their email, which is unique across the whole system). This is the one legitimate, isolated use of `.IgnoreQueryFilters()` in the codebase — confined to `IUserLookupService.FindByEmailAcrossTenantsAsync(email)`, used only by the login handler, with a comment explaining why. No other code path may call `IgnoreQueryFilters()`.

## Repository + UnitOfWork

Adopted deliberately (acknowledged trade-off: EF Core's `DbContext`/`DbSet<T>` already are a unit of work and a repository; this is an extra layer some consider redundant — kept anyway as the user's explicit choice/practice goal). Corrected from a past project's version, which had a real bug: each repository method created and disposed its own `DbContext` via `IDbContextFactory`, so `Add()` and the later `CompleteAsync()`'s `SaveChangesAsync()` operated on two different, already-disposed context instances — nothing was ever actually persisted. That pattern existed there to keep parity with a parallel MongoDB implementation (which has no change-tracker concept); it doesn't apply here since Lumen is Postgres-only.

**Lumen's version:** one shared, scoped `AppDbContext` instance injected into every repository and the `UnitOfWork` for the same request. Repositories operate on **domain models**, not entities — internally they map to/from `TEntity` via AutoMapper and call `context.Set<TEntity>()`, so the tenant query filter is applied transparently (repositories never see `ITenantEntity`/filter details).

```csharp
public interface IRepository<TDomain>
{
    void Add(TDomain entity);
    Task<TDomain?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<TDomain>> GetAllAsync(CancellationToken ct = default);
    void Remove(TDomain entity);
}

public interface IUnitOfWork
{
    IRepository<Document> Documents { get; }
    Task<int> CompleteAsync(CancellationToken ct = default);
}
```

`UnitOfWork.CompleteAsync()` calls `SaveChangesAsync` on the *same* injected `AppDbContext` that every repository used — this is what actually fixes the bug.

## Auth

- Self-issued JWT — no ASP.NET Core Identity, no external IdP.
- `AddAuthentication().AddJwtBearer(...)` validates tokens; claims: `sub` (user id), `tenant_id`, `role`.
- Roles: `Admin` (can ingest — relevant from stage 2 on) vs `Member` (query only).
- Password hashing via built-in `PasswordHasher<User>` (no full Identity system needed for just this).
- `POST /auth/token` — login endpoint (`AuthController`), looks up user across tenants (see login exception above), verifies password, issues JWT.
- Seed data (via EF Core `HasData` or a startup seeding step in Development) creates a couple of `Tenant`/`User` pairs so there's something to log in as and test isolation against, out of the box.
- `/register` and `/tenants` self-service endpoints are deliberately deferred — seed data is enough to prove the mechanism for stage 1.

## API layer

- Controllers (not Minimal APIs) — `DocumentsController`, `AuthController`.
- Swagger/OpenAPI with JWT bearer support configured (so tokens can be tested directly from Swagger UI).
- `Program.cs` composition root: DI registration for MediatR, AutoMapper (both profiles), EF Core (`AppDbContext`, non-pooled), repositories/UnitOfWork, JWT auth, `TenantResolutionMiddleware`, global exception handling.

## MediatR / CQRS

- MediatR (Community-tier license — free for a personal, non-production, <$5M-revenue project; same terms as the AutoMapper announcement).
- Commands (`CreateDocumentCommand`) and Queries (`GetDocumentByIdQuery`, `GetDocumentsQuery`) with matching handlers in `Lumen.Application`.
- Handler flow: map incoming Dto/command fields → domain model → call repository/UnitOfWork → map result back to Dto for the response.
- No FluentValidation for stage 1 (YAGNI at this scale) — data annotations on Dtos are enough; revisit if validation logic grows non-trivial.

## Error handling

Built-in `IExceptionHandler` (the .NET 8+ idiomatic replacement for hand-rolled exception middleware), registered via `AddExceptionHandler<GlobalExceptionHandler>()` + `AddProblemDetails()`, wired via `app.UseExceptionHandler()`. Maps custom domain exceptions to HTTP status + `ProblemDetails` body:

| Exception | Status |
|---|---|
| `NotFoundException` | 404 |
| `ForbiddenException` | 403 |
| `ConflictException` | 409 |
| `ValidationException` | 400 |
| `UnauthorizedException` (bad login credentials) | 401 |
| anything else | 500, generic message (don't leak internals) |

## Local development

`docker-compose.yml` at repo root: single `postgres` service + named volume. If the default `5432` is already taken locally, remap the host port in the compose file rather than anything more elaborate.

## Testing

- **Integration** (`Lumen.IntegrationTests`, xUnit + `Testcontainers.PostgreSql`, real Postgres container, no mocks): seed two tenants, each with their own `Document`; assert that operating as tenant A never returns tenant B's `Document` through the repository/DbContext. This is the stage-1-level version of the isolation guarantee — the harder raw-SQL version (where the global filter structurally cannot apply, per `.research/ef-core-raw-sql-tenant-isolation.md`) is stage 3's job, once the vector query exists.
- **Unit** (`Lumen.UnitTests`): MediatR handlers with mocked `IUnitOfWork`/repositories; AutoMapper profile validation (`AssertConfigurationIsValid()`) for both profiles.

## Deferred / open items

Left for later stages or later in this stage, not designed now: exact JWT expiry/refresh-token strategy (a sensible short-lived-access-token default is fine to start, revisit if it becomes friction), whether Row-Level Security gets added as defense-in-depth (noted as a "when I'd reconsider" candidate for `DECISIONS.md`, not built now).
