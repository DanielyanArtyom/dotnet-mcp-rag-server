# Lumen — C# project conventions specification

Status: in effect, applies to all six projects (`Lumen.Domain`, `Lumen.Application`, `Lumen.Infrastructure`, `Lumen.Api`, `Lumen.UnitTests`, `Lumen.IntegrationTests`). Established after Stage 1's 13 tasks were complete, in a dedicated review-and-refactor pass over Api + Application first, then Infrastructure, then the two test projects. Domain needed no changes (four dependency-free POCOs, nothing to restructure). The terse, living version of these rules lives in `CLAUDE.md`'s "C# code style conventions" section — this document carries the fuller rationale so the *why* survives even when the CLAUDE.md bullet doesn't have room for it.

## 1. Onion layering: DTOs live only at the API boundary

- `Lumen.Application`'s commands/queries/handlers accept and return **Domain models** (`Lumen.Domain`) or plain primitives — never a Dto type.
- Dto types (`DocumentDto`, `LoginRequest`, `TokenResponse`, …) live in `Lumen.Api/DTO/`, split into `Requests/` and `Responses/` subfolders by direction.
- The Api layer (controllers) is the only place that maps Domain → Dto for a response (via `IMapper`, using AutoMapper profiles that themselves live in `Lumen.Api/Mapping/` — e.g. `DomainDtoMappingProfile`) and Dto → command/query input at the request-binding boundary.
- **Why:** keeps Application ignorant of transport concerns (JSON shape, validation attributes meant for model binding) and keeps the Dto surface — the thing external clients actually see — in one place, next to the controllers that expose it. This was a mid-course correction: Stage 1's original 13-task plan put `DocumentDto` in `Lumen.Application.Documents` and had `CreateDocumentCommand`/`GetDocumentsQuery`/`GetDocumentByIdQuery` return `DocumentDto` directly, with `IMapper` injected straight into the MediatR handlers. Moving the Dto out simplified those handlers too — they lost the `IMapper` dependency entirely, since Application no longer does any Domain↔Dto translation.

## 2. Interfaces, not Abstractions

`Lumen.Application`'s interface folder is `Interfaces/` (namespace `Lumen.Application.Interfaces`), not `Abstractions/`. Both names are common in Onion/Clean Architecture references — this is a naming preference, not a correctness rule — but once chosen, it's applied consistently: every consumer across Infrastructure, Api, and the test projects references `Lumen.Application.Interfaces`.

## 3. Constructor injection: traditional constructor + readonly fields

Classes **constructed via DI** — controllers, MediatR command/query handlers, `IExceptionHandler` implementations, ASP.NET Core middleware — use:

```csharp
public class DocumentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public DocumentsController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }
    ...
}
```

not C# primary-constructor parameters (`public class DocumentsController(IMediator mediator) : ControllerBase`), even though the latter is shorter. This is a project-wide style preference, applied retroactively across every DI-constructed class in Api, Application, and Infrastructure (`DocumentsController`, `AuthController`, `GlobalExceptionHandler`, `TenantResolutionMiddleware`, every MediatR handler, `UserLookupService`, `JwtTokenService`, `DocumentRepository`, `UnitOfWork`).

**What this rule does *not* cover:** `WebApplicationBuilder`/`IServiceCollection`/`IApplicationBuilder` extension methods (everything in an `Extensions/` or `DependencyInjection/` folder, per §5 below) are `static` classes — there is no DI, no instance being constructed, so primary-constructor syntax simply doesn't apply to them. xUnit test classes taking a fixture via `ClassName(FixtureType fixture)` are a separate, well-established xUnit idiom (constructor-per-test-instance) and are also unaffected — this rule is about production DI, not test-framework conventions.

## 4. `using` directives

### 4.1 GlobalUsings.cs and the 2+-file promotion rule

- Every project has its own `GlobalUsings.cs` at its root.
- A namespace is promoted to `global using` in that file once it's referenced in **more than one file** in that project — not before. A namespace needed in exactly one file stays a local `using` in that file.
- When a new `using` is added anywhere, check whether the namespace is already used elsewhere in the same project (or will be, after the change, in 2+ files). If so, promote it and remove the now-redundant local `using` everywhere in the project — never leave it duplicated in both places.
- Never leave an unused `using`, local or global. No analyzer is wired into the build for this — it has to be checked by hand (or via a temporary `.editorconfig` enabling `dotnet_diagnostic.IDE0005.severity = warning` + `dotnet format style --diagnostics IDE0005`, removed again afterward — this is the method used throughout Stage 1's cleanup passes).
- Don't duplicate a namespace the SDK already injects implicitly (`ImplicitUsings` is `enable` everywhere) — `System`, `System.Linq`, `System.Threading.Tasks`, and, on `Lumen.Api`, the ASP.NET Core namespaces the `Microsoft.NET.Sdk.Web` SDK adds automatically.

**Auditing method, learned the hard way:** a simple per-file grep of `using` lines only tells you what ISN'T yet global — it can't catch a namespace that got over-promoted (used in only 1 file but sitting in `GlobalUsings.cs` anyway) mid-refactor, because a file relying on that over-promoted global has *no local line to grep*. The reliable method when a project's usings have been substantially reshuffled: temporarily empty `GlobalUsings.cs`, run a full build, and read the CS0246 "type or namespace not found" errors — each one names the exact file and missing symbol, from which the true owning namespace and its real per-file usage count can be reconstructed without trusting any existing (possibly already-wrong) file.

### 4.2 GlobalUsings.cs internal grouping

Within one `GlobalUsings.cs`, entries are grouped into up to four blocks, separated by exactly one blank line, in this fixed order:

1. `Lumen.*`
2. `Microsoft.*`
3. `System.*`
4. everything else (third-party packages — `AutoMapper`, `MediatR`, `Moq`, `NLog.Web`, `Testcontainers.*`, …)

A project with nothing in a given group simply omits that block (no empty block with a dangling blank line). Order *within* a block isn't enforced. Every time a `using` is added or promoted, it goes into its matching block — never just appended at the end of the file.

### 4.3 Don't fully-qualify a type when a `using` would do

`Lumen.Domain.User` (or the partial form `Domain.User`) in a file that could just say `using Lumen.Domain;` and then read `User` is noise. Add the `using` — local if the namespace is used in only this file, promoted per §4.1 if it crosses into a second file — and use the bare name. Fully-qualify only to resolve a genuine ambiguity between two types that share a short name.

## 5. Folder conventions: `Extensions/`, `DependencyInjection/`, `Constants/`

Modeled on an existing reference project (`GameStore.API`/`GameStore.Business` in a sibling repo) rather than invented from scratch:

- **`Lumen.Api/Extensions/`** — two static classes:
  - `ApplicationBuilderExtension` — post-`Build()` pipeline setup: `UseDatabaseMigrationAsync(this WebApplication app)` (Development-only migrate+seed) and `UseMiddlewares(this IApplicationBuilder app)` (wraps custom `UseMiddleware<T>()` calls — currently just `TenantResolutionMiddleware`, with room for more without touching `Program.cs` again).
  - `WebApplicationBuilderExtensions` — pre-`Build()` service registration, one method per concern, chained fluently in `Program.cs`: `Configure()` (options binding, `AddDbContext`, `AddMediatR`), `AddLogger()` (NLog wiring), `AddWebApi()` (`AddControllers`, `AddEndpointsApiExplorer`, the exception-handler registration), `AddMapper()` (`AddAutoMapper`), `AddAuthentication()` (JWT bearer + `AddAuthorization`), `AddSwagger()` (`AddSwaggerGen` + the JWT bearer security scheme).
- **`Lumen.Infrastructure/DependencyInjection/`** — `ServiceCollectionExtensions.AddLumenServices(this IServiceCollection)`, holding every `AddScoped`/`AddSingleton` that isn't handled by the Api-layer `Configure()` above (tenant context, repositories/UnitOfWork, auth services). Called once from `Program.cs` (`builder.Services.AddLumenServices();`), before the `WebApplicationBuilder` extension-method chain.
- **`<Project>/DependencyInjection/`** also holds that project's `Options`-bound classes (e.g. `Lumen.Infrastructure/DependencyInjection/JwtOptions.cs`) — the binding/config concern belongs with the rest of that project's DI wiring, not next to the service that happens to consume the bound values.
- **`<Project>/Constants/`** holds named constants for magic strings that repeat across files within that project (e.g. `Lumen.Infrastructure/Constants/JwtClaimConstants.cs` for the `"tenant_id"` JWT claim name and the `"Jwt"` config section name — both were scattered literals across `JwtOptions`, `JwtTokenService`, and Api's `TenantResolutionMiddleware`/`WebApplicationBuilderExtensions` before). Apply per-project as new cross-file literals appear; a string used in exactly one place doesn't need a constant.
  - **Naming pitfall to watch for:** a constants class name can collide with an existing BCL/framework type in an unrelated namespace that's already in scope (`JwtConstants` collided with `System.IdentityModel.Tokens.Jwt.JwtConstants` — caught immediately by the compiler as `CS0104 ambiguous reference`, renamed to `JwtClaimConstants`). Not a reason to avoid the pattern, just something to check for.

`Program.cs` itself is left as just: `builder.Services.AddLumenServices();`, the fluent `WebApplicationBuilder` extension-method chain, `builder.Build()`, then the app-pipeline calls (`UseExceptionHandler`, the Development-only Swagger UI block, `await app.UseDatabaseMigrationAsync();`, `UseAuthentication`/`UseMiddlewares`/`UseAuthorization`, `MapControllers`, `Run`).

## 6. Routing: `[Route("api/[controller]")]`

Both controllers (`AuthController`, `DocumentsController`) use the conventional `[Route("api/[controller]")]` template rather than a literal per-controller route string (`[Route("auth")]`, `[Route("documents")]`). Actual routes: `POST /api/auth/token`, `GET/POST /api/documents`, `GET /api/documents/{id}`. Applies to every controller added going forward. `CreatedAtAction(nameof(GetById), ...)`-style Location-header generation needs no special handling — it resolves from route metadata, not a literal path.

## 7. Comments: default to none

Only add a comment when the *why* is genuinely non-obvious: a hidden constraint, a workaround for a specific library/framework bug, an invariant a future edit could silently break. Never narrate *what* the code does, and never reference the task/review that produced the change. Concrete examples from this codebase where a comment earned its place: `UserLookupService`'s note that it's the only legal `.IgnoreQueryFilters()` call site; `CreateDocumentCommand`'s note that validation attributes must target the record's constructor parameters, not `[property: ...]`, because the latter makes MVC's `ValidationVisitor` throw and turns every `POST /api/documents` into a 500.

## 8. Project references: one straight line, relying on transitivity

`Lumen.Api` references only `Lumen.Infrastructure`; `Lumen.Infrastructure` references only `Lumen.Application`; `Lumen.Application` references only `Lumen.Domain`. SDK-style `ProjectReference` is transitive by default, so each project still has full compile-time access to everything below it in the chain — this was verified by building after removing the redundant explicit references, not assumed. Test projects (`Lumen.UnitTests`, `Lumen.IntegrationTests`) were left with their existing multi-project references — the simplification was scoped to the four `src/` projects only.

## Things this pass deliberately did *not* change

- **`AppDbContextFactory` was removed entirely, not folded into these conventions** — it turned out to be unnecessary. `dotnet ef` tooling on this .NET version builds the real `Program.cs`/`WebApplication` host (including `AddLumenServices()`, which registers `ICurrentTenant`) rather than needing a bespoke `IDesignTimeDbContextFactory<AppDbContext>`; verified directly by running `dotnet ef dbcontext info` and `dotnet ef migrations list` with the factory temporarily removed, both of which read real config and hit the real (mis)configured Postgres target, proving the real host — not a fallback — was doing the work.
- **A schema-level foreign key from `Documents.TenantId`/`Users.TenantId` to `Tenants.Id`** was added (`OnDelete(DeleteBehavior.Restrict)`), requiring three existing integration tests to be updated to seed a real `Tenants` row first (they previously used ad-hoc `TenantId` GUIDs with no matching tenant row, which the new FK would otherwise reject).
- **`AppDbContext.SaveChanges`'s tenant-stamping logic was made unconditional** — every `SaveChanges`/`SaveChangesAsync` overload now always overwrites a newly-added tenant-owned entity's `TenantId` from the ambient `ICurrentTenant`, rather than only when the property was left at `Guid.Empty`. `Lumen.Infrastructure/Tenancy/TenantScopedWriter` exists specifically to let code that legitimately needs to write rows for a *specific* tenant outside the normal per-request flow (seed data creating two tenants' worth of rows in one pass) point the shared, mutable `CurrentTenant` at that tenant for the duration of one write, then restore it — sequentially, never via `Task.WhenAll`, since two calls would otherwise race on both the shared `AppDbContext` (not thread-safe for concurrent operations) and the shared `CurrentTenant`.
- **`Guid.NewGuid()` for new `Document`s stays in `Lumen.Application`'s `CreateDocumentCommandHandler`**, not removed in favor of relying on EF Core's own Guid-key generation convention — verified empirically (a throwaway Testcontainers-backed probe) that while EF Core *does* generate a Guid for an unset key at `SaveChanges` time, it mutates the tracked `DocumentEntity` that `DocumentRepository.Add`'s `AutoMapper.Map<DocumentEntity>()` call creates as a *new* object — the value never flows back onto the original `Document` domain object the handler returns, so the API response would carry an empty Id if the manual assignment were removed.
