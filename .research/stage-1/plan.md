# Lumen Stage 1 (Skeleton) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Status (2026-09-11): all 13 tasks below are done and were reviewed** (per-task + final whole-branch review). This file is kept as-written — it's the record of what was planned and executed task-by-task, and the checkboxes/task text below still describe that history accurately. A separate post-completion review pass then changed several structural choices that this plan's task text does NOT reflect (Dto location, constructor style, folder layout, routing, etc.) — those aren't plan defects, they're later decisions made by reading the finished code. Full list: the amendment note at the top of `design.md`, full rationale in `.research/specification/csharp-project-conventions.md`, narrative in `.research/stage-1/progress.md`. Don't use this file's task text alone as a description of the current codebase — cross-check against those three.

**Goal:** Stand up the Lumen Onion-Architecture solution with working multi-tenancy (TenantId discriminator + EF Core global query filters), self-issued JWT auth, and one real end-to-end vertical slice (`Document` CRUD) that proves tenant isolation actually holds — not just that the solution compiles.

**Architecture:** Four-project Onion solution (`Domain` → `Application` → `Infrastructure` → `Api`), CQRS via MediatR, full DTO/Domain-model/EF-entity separation mapped with AutoMapper, Repository+UnitOfWork over a single shared scoped `AppDbContext` (Postgres). Tenant identity flows from a JWT `tenant_id` claim through a request middleware into a scoped `ICurrentTenant`, which both the EF global query filter and the entity `SaveChangesAsync` auto-stamp read from.

**Tech Stack:** .NET 10, ASP.NET Core (Controllers + Swagger), EF Core 10 + Npgsql.EntityFrameworkCore.PostgreSQL, PostgreSQL (Docker), MediatR (Community license), AutoMapper (Community license), `Microsoft.AspNetCore.Authentication.JwtBearer`, `PasswordHasher<T>` (`Microsoft.Extensions.Identity.Core`), xUnit, Testcontainers.PostgreSql, Moq.

**Spec:** `.research/stage-1/design.md`

## Global Constraints

- Target framework: **.NET 10** on every project.
- Database: **PostgreSQL** only — no SQL Server anywhere in this stage.
- `AppDbContext` is registered via **`AddDbContext`**, never `AddDbContextPool`/`AddPooledDbContextFactory` (pooling can leak a captured tenant across requests — see spec).
- `.IgnoreQueryFilters()` may appear in **exactly one place** in the whole codebase: `UserLookupService.FindByEmailAcrossTenantsAsync`. Any other occurrence is a bug.
- No `DocumentChunk`, no embeddings, no PDF parsing, no MCP, no `/register`/`/tenants` endpoints in this stage — out of scope per spec, deferred to later stages.
- No FluentValidation — data annotations are enough at this scale (YAGNI, per spec).

---

## Task 1: Solution & Project Scaffolding

**Files:**
- Create: `Lumen.sln`
- Create: `src/Lumen.Domain/Lumen.Domain.csproj`
- Create: `src/Lumen.Application/Lumen.Application.csproj`
- Create: `src/Lumen.Infrastructure/Lumen.Infrastructure.csproj`
- Create: `src/Lumen.Api/Lumen.Api.csproj`
- Create: `tests/Lumen.UnitTests/Lumen.UnitTests.csproj`
- Create: `tests/Lumen.IntegrationTests/Lumen.IntegrationTests.csproj`
- Create: `docker-compose.yml`
- Modify: `.gitignore` (already has `bin/`, `obj/`, `.vs/` etc. — confirm, don't duplicate)

**Interfaces:**
- Produces: a solution that builds (`dotnet build`), with project references wired per the dependency rule `Domain ← Application ← Infrastructure ← Api`, and both test projects referencing what they need. No code yet beyond scaffolding.

- [ ] **Step 1: Create the solution and all projects**

```bash
dotnet new sln -n Lumen

dotnet new classlib -n Lumen.Domain -o src/Lumen.Domain -f net10.0
dotnet new classlib -n Lumen.Application -o src/Lumen.Application -f net10.0
dotnet new classlib -n Lumen.Infrastructure -o src/Lumen.Infrastructure -f net10.0
dotnet new webapi -n Lumen.Api -o src/Lumen.Api -f net10.0 --use-controllers

dotnet new xunit -n Lumen.UnitTests -o tests/Lumen.UnitTests -f net10.0
dotnet new xunit -n Lumen.IntegrationTests -o tests/Lumen.IntegrationTests -f net10.0

dotnet sln add src/Lumen.Domain src/Lumen.Application src/Lumen.Infrastructure src/Lumen.Api tests/Lumen.UnitTests tests/Lumen.IntegrationTests
```

- [ ] **Step 2: Wire project references**

```bash
dotnet add src/Lumen.Application reference src/Lumen.Domain
dotnet add src/Lumen.Infrastructure reference src/Lumen.Application src/Lumen.Domain
dotnet add src/Lumen.Api reference src/Lumen.Infrastructure src/Lumen.Application src/Lumen.Domain

dotnet add tests/Lumen.UnitTests reference src/Lumen.Application src/Lumen.Infrastructure src/Lumen.Domain
dotnet add tests/Lumen.IntegrationTests reference src/Lumen.Api src/Lumen.Infrastructure src/Lumen.Application src/Lumen.Domain
```

- [ ] **Step 3: Add NuGet packages per project**

```bash
# Application
dotnet add src/Lumen.Application package MediatR
dotnet add src/Lumen.Application package AutoMapper

# Infrastructure
dotnet add src/Lumen.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/Lumen.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add src/Lumen.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Lumen.Infrastructure package Microsoft.Extensions.Identity.Core
dotnet add src/Lumen.Infrastructure package System.IdentityModel.Tokens.Jwt
dotnet add src/Lumen.Infrastructure package AutoMapper

# Api
dotnet add src/Lumen.Api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Lumen.Api package Swashbuckle.AspNetCore

# UnitTests
dotnet add tests/Lumen.UnitTests package Moq
dotnet add tests/Lumen.UnitTests package AutoMapper

# IntegrationTests
dotnet add tests/Lumen.IntegrationTests package Testcontainers.PostgreSql
dotnet add tests/Lumen.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
```

If `builder.Services.AddAutoMapper(...)` isn't found after adding the `AutoMapper` package (packaging has moved around across major versions), also add `dotnet add src/Lumen.Api package AutoMapper.Extensions.Microsoft.DependencyInjection`.

- [ ] **Step 4: Add `docker-compose.yml` for local Postgres**

```yaml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: lumen
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - lumen-postgres-data:/var/lib/postgresql/data

volumes:
  lumen-postgres-data:
```

If port `5432` is already taken locally, change the host side only, e.g. `"5433:5432"`, and update `ConnectionStrings:Postgres` in `src/Lumen.Api/appsettings.Development.json` (added in Task 4) to match.

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build`
Expected: `Build succeeded.` with 0 errors (warnings about unused default template files are fine — they get replaced in later tasks).

- [ ] **Step 6: Commit**

```bash
git add Lumen.sln src/ tests/ docker-compose.yml
git commit -m "chore: scaffold Onion solution structure"
```

---

## Task 2: Domain Layer

**Files:**
- Create: `src/Lumen.Domain/Role.cs`
- Create: `src/Lumen.Domain/Tenant.cs`
- Create: `src/Lumen.Domain/User.cs`
- Create: `src/Lumen.Domain/Document.cs`
- Test: `tests/Lumen.UnitTests/Domain/DocumentTests.cs`

**Interfaces:**
- Produces: `Lumen.Domain.Role { Admin, Member }`, `Lumen.Domain.Tenant { Guid Id, string Name }`, `Lumen.Domain.User { Guid Id, Guid TenantId, string Email, string PasswordHash, Role Role }`, `Lumen.Domain.Document { Guid Id, Guid TenantId, string Title, string Content }`. Every later task's Domain-level references use exactly these shapes.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.UnitTests/Domain/DocumentTests.cs
using Lumen.Domain;
using Xunit;

namespace Lumen.UnitTests.Domain;

public class DocumentTests
{
    [Fact]
    public void Document_holds_the_values_it_was_given()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var document = new Document
        {
            Id = id,
            TenantId = tenantId,
            Title = "Test title",
            Content = "Test content"
        };

        Assert.Equal(id, document.Id);
        Assert.Equal(tenantId, document.TenantId);
        Assert.Equal("Test title", document.Title);
        Assert.Equal("Test content", document.Content);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.UnitTests --filter DocumentTests`
Expected: FAIL — compile error, `Document` does not exist yet.

- [ ] **Step 3: Write the Domain classes**

```csharp
// src/Lumen.Domain/Role.cs
namespace Lumen.Domain;

public enum Role
{
    Admin,
    Member
}
```

```csharp
// src/Lumen.Domain/Tenant.cs
namespace Lumen.Domain;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
}
```

```csharp
// src/Lumen.Domain/User.cs
namespace Lumen.Domain;

public class User
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public Role Role { get; set; }
}
```

```csharp
// src/Lumen.Domain/Document.cs
namespace Lumen.Domain;

public class Document
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Lumen.UnitTests --filter DocumentTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Lumen.Domain tests/Lumen.UnitTests
git commit -m "feat: add Domain models (Tenant, User, Document, Role)"
```

---

## Task 3: Persistence — Entities, Configurations, AppDbContext with tenant filter

**Files:**
- Create: `src/Lumen.Infrastructure/Persistence/ITenantEntity.cs`
- Create: `src/Lumen.Infrastructure/Persistence/Entities/TenantEntity.cs`
- Create: `src/Lumen.Infrastructure/Persistence/Entities/UserEntity.cs`
- Create: `src/Lumen.Infrastructure/Persistence/Entities/DocumentEntity.cs`
- Create: `src/Lumen.Infrastructure/Persistence/Configurations/TenantEntityConfiguration.cs`
- Create: `src/Lumen.Infrastructure/Persistence/Configurations/UserEntityConfiguration.cs`
- Create: `src/Lumen.Infrastructure/Persistence/Configurations/DocumentEntityConfiguration.cs`
- Create: `src/Lumen.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/Lumen.Infrastructure/Tenancy/CurrentTenant.cs`
- Create: `src/Lumen.Application/Abstractions/ICurrentTenant.cs`
- Test: `tests/Lumen.IntegrationTests/Persistence/TenantIsolationDbContextTests.cs`
- Test: `tests/Lumen.IntegrationTests/PostgresFixture.cs`

**Interfaces:**
- Consumes: `Lumen.Domain.Document`, `Lumen.Domain.User`, `Lumen.Domain.Tenant` (Task 2).
- Produces: `ICurrentTenant { Guid TenantId }` (Application), `CurrentTenant : ICurrentTenant` with a **settable** `TenantId` (Infrastructure), `ITenantEntity { Guid TenantId get; set; }`, `AppDbContext` with `DbSet<TenantEntity> Tenants`, `DbSet<UserEntity> Users`, `DbSet<DocumentEntity> Documents`. Every later task that touches the database goes through `AppDbContext` exactly as defined here.

- [ ] **Step 1: Add the Testcontainers Postgres fixture (shared by all integration tests)**

```csharp
// tests/Lumen.IntegrationTests/PostgresFixture.cs
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lumen.IntegrationTests;

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("lumen_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString);

        await using var context = new AppDbContext(optionsBuilder.Options, new CurrentTenant());
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("Postgres collection")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

This won't compile until `AppDbContext`, `CurrentTenant`, and migrations exist — that's expected, keep going; migrations land in Task 4, so this fixture's `MigrateAsync()` call will find zero migrations until then. That's fine, `MigrateAsync()` on an empty migration set is a no-op.

- [ ] **Step 2: Write the failing tenant-isolation test**

```csharp
// tests/Lumen.IntegrationTests/Persistence/TenantIsolationDbContextTests.cs
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Persistence.Entities;
using Lumen.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lumen.IntegrationTests.Persistence;

[Collection("Postgres collection")]
public class TenantIsolationDbContextTests(PostgresFixture fixture)
{
    private AppDbContext CreateContext(Guid tenantId)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(fixture.ConnectionString);
        return new AppDbContext(optionsBuilder.Options, new CurrentTenant { TenantId = tenantId });
    }

    [Fact]
    public async Task Tenant_A_never_sees_Tenant_Bs_documents()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await using (var seedContext = CreateContext(tenantAId))
        {
            seedContext.Documents.Add(new DocumentEntity { Id = Guid.NewGuid(), TenantId = tenantAId, Title = "A doc", Content = "belongs to A" });
            await seedContext.SaveChangesAsync();
        }

        await using (var seedContext = CreateContext(tenantBId))
        {
            seedContext.Documents.Add(new DocumentEntity { Id = Guid.NewGuid(), TenantId = tenantBId, Title = "B doc", Content = "belongs to B" });
            await seedContext.SaveChangesAsync();
        }

        await using var contextAsTenantA = CreateContext(tenantAId);
        var visibleToA = await contextAsTenantA.Documents.ToListAsync();

        Assert.Single(visibleToA);
        Assert.All(visibleToA, d => Assert.Equal(tenantAId, d.TenantId));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Lumen.IntegrationTests --filter TenantIsolationDbContextTests`
Expected: FAIL — compile error, `ITenantEntity`, entity classes, and `AppDbContext` don't exist yet.

- [ ] **Step 4: Write `ITenantEntity` and the entity classes**

```csharp
// src/Lumen.Infrastructure/Persistence/ITenantEntity.cs
namespace Lumen.Infrastructure.Persistence;

public interface ITenantEntity
{
    Guid TenantId { get; set; }
}
```

```csharp
// src/Lumen.Infrastructure/Persistence/Entities/TenantEntity.cs
namespace Lumen.Infrastructure.Persistence.Entities;

public class TenantEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
}
```

```csharp
// src/Lumen.Infrastructure/Persistence/Entities/UserEntity.cs
using Lumen.Domain;

namespace Lumen.Infrastructure.Persistence.Entities;

public class UserEntity : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public Role Role { get; set; }
}
```

```csharp
// src/Lumen.Infrastructure/Persistence/Entities/DocumentEntity.cs
namespace Lumen.Infrastructure.Persistence.Entities;

public class DocumentEntity : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
}
```

- [ ] **Step 5: Write the EF configurations**

```csharp
// src/Lumen.Infrastructure/Persistence/Configurations/TenantEntityConfiguration.cs
using Lumen.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lumen.Infrastructure.Persistence.Configurations;

public class TenantEntityConfiguration : IEntityTypeConfiguration<TenantEntity>
{
    public void Configure(EntityTypeBuilder<TenantEntity> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
    }
}
```

```csharp
// src/Lumen.Infrastructure/Persistence/Configurations/UserEntityConfiguration.cs
using Lumen.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lumen.Infrastructure.Persistence.Configurations;

public class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(320);
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.HasIndex(u => u.TenantId);
    }
}
```

```csharp
// src/Lumen.Infrastructure/Persistence/Configurations/DocumentEntityConfiguration.cs
using Lumen.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lumen.Infrastructure.Persistence.Configurations;

public class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
{
    public void Configure(EntityTypeBuilder<DocumentEntity> builder)
    {
        builder.ToTable("Documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Title).IsRequired().HasMaxLength(500);
        builder.Property(d => d.Content).IsRequired();
        builder.HasIndex(d => d.TenantId);
    }
}
```

- [ ] **Step 6: Write `ICurrentTenant` and `CurrentTenant`**

```csharp
// src/Lumen.Application/Abstractions/ICurrentTenant.cs
namespace Lumen.Application.Abstractions;

public interface ICurrentTenant
{
    Guid TenantId { get; }
}
```

```csharp
// src/Lumen.Infrastructure/Tenancy/CurrentTenant.cs
using Lumen.Application.Abstractions;

namespace Lumen.Infrastructure.Tenancy;

public class CurrentTenant : ICurrentTenant
{
    public Guid TenantId { get; set; }
}
```

- [ ] **Step 7: Write `AppDbContext`**

```csharp
// src/Lumen.Infrastructure/Persistence/AppDbContext.cs
using Lumen.Application.Abstractions;
using Lumen.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lumen.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly ICurrentTenant _currentTenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant currentTenant)
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<UserEntity>().HasQueryFilter(u => u.TenantId == _currentTenant.TenantId);
        modelBuilder.Entity<DocumentEntity>().HasQueryFilter(d => d.TenantId == _currentTenant.TenantId);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Only fill in TenantId if it wasn't already set explicitly (e.g. by seed
        // data creating rows for multiple tenants at once). Normal application code
        // leaves it as Guid.Empty and relies on this stamp.
        foreach (var entry in ChangeTracker.Entries<ITenantEntity>()
                     .Where(e => e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty))
        {
            entry.Entity.TenantId = _currentTenant.TenantId;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/Lumen.IntegrationTests --filter TenantIsolationDbContextTests`
Expected: still FAIL at this point — `Documents.ToListAsync()` needs a real table, and there's no migration yet. This is expected; Task 4 adds the migration. Confirm the failure is now a runtime "relation does not exist" error, not a compile error — that confirms everything up to here is wired correctly.

- [ ] **Step 9: Commit**

```bash
git add src/Lumen.Infrastructure src/Lumen.Application/Abstractions/ICurrentTenant.cs tests/Lumen.IntegrationTests
git commit -m "feat: add entities, EF configurations, and tenant-filtered AppDbContext"
```

---

## Task 4: EF Core Migrations

**Files:**
- Create: `src/Lumen.Infrastructure/Persistence/AppDbContextFactory.cs`
- Create: `src/Lumen.Api/appsettings.json`
- Create: `src/Lumen.Api/appsettings.Development.json`
- Create: `src/Lumen.Infrastructure/Persistence/Migrations/*` (generated)

**Interfaces:**
- Consumes: `AppDbContext` (Task 3).
- Produces: an applied migration; `tests/Lumen.IntegrationTests`' `PostgresFixture.MigrateAsync()` (Task 3) now creates real tables.

- [ ] **Step 1: Add the design-time factory (needed because `AppDbContext`'s constructor takes `ICurrentTenant`, which the EF CLI tooling can't resolve on its own)**

```csharp
// src/Lumen.Infrastructure/Persistence/AppDbContextFactory.cs
using Lumen.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lumen.Infrastructure.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=lumen;Username=postgres;Password=postgres");
        return new AppDbContext(optionsBuilder.Options, new CurrentTenant());
    }
}
```

- [ ] **Step 2: Add connection string configuration to the Api project**

```json
// src/Lumen.Api/appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Jwt": {
    "Issuer": "Lumen",
    "Audience": "Lumen",
    "ExpiryMinutes": 60
  }
}
```

```json
// src/Lumen.Api/appsettings.Development.json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=lumen;Username=postgres;Password=postgres"
  },
  "Jwt": {
    "Secret": "dev-only-secret-change-me-32-bytes-minimum-for-hs256"
  }
}
```

`Jwt:Secret` for HS256 must be at least 32 bytes/256 bits — the placeholder above is long enough; treat it as dev-only and never commit a production secret (production values come from environment/managed identity in Stage 6, not from this file).

- [ ] **Step 3: Generate the initial migration**

```bash
dotnet ef migrations add InitialCreate --project src/Lumen.Infrastructure --startup-project src/Lumen.Api --output-dir Persistence/Migrations
```

Expected: creates `src/Lumen.Infrastructure/Persistence/Migrations/<timestamp>_InitialCreate.cs` and a model snapshot, with `CreateTable` calls for `Tenants`, `Users`, `Documents`.

If this fails with "Unable to create an object of type 'AppDbContext'", double check `AppDbContextFactory` is public and in the `Lumen.Infrastructure` project, and that `Microsoft.EntityFrameworkCore.Design` was added to that project in Task 1.

- [ ] **Step 4: Re-run the Task 3 isolation test to confirm the migration actually creates working tables**

Run: `dotnet test tests/Lumen.IntegrationTests --filter TenantIsolationDbContextTests`
Expected: PASS — `PostgresFixture` now applies the real migration on container startup, so `Tenant_A_never_sees_Tenant_Bs_documents` passes end to end.

- [ ] **Step 5: Commit**

```bash
git add src/Lumen.Infrastructure/Persistence/AppDbContextFactory.cs src/Lumen.Infrastructure/Persistence/Migrations src/Lumen.Api/appsettings.json src/Lumen.Api/appsettings.Development.json
git commit -m "feat: add initial EF Core migration"
```

---

## Task 5: AutoMapper Profiles & DTOs

**Files:**
- Create: `src/Lumen.Infrastructure/Mapping/DomainEntityMappingProfile.cs`
- Create: `src/Lumen.Application/Documents/DocumentDto.cs`
- Create: `src/Lumen.Application/Mapping/DomainDtoMappingProfile.cs`
- Test: `tests/Lumen.UnitTests/Mapping/MappingProfileTests.cs`

**Interfaces:**
- Consumes: `Document`, `User`, `Tenant` (Domain, Task 2), `DocumentEntity`, `UserEntity`, `TenantEntity` (Infrastructure, Task 3).
- Produces: `DocumentDto { Guid Id, string Title, string Content }` — every later Application/Api task that returns a document to a client uses exactly this shape. Two validated AutoMapper profiles registered by name: `DomainEntityMappingProfile`, `DomainDtoMappingProfile`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.UnitTests/Mapping/MappingProfileTests.cs
using AutoMapper;
using Lumen.Application.Mapping;
using Lumen.Infrastructure.Mapping;
using Xunit;

namespace Lumen.UnitTests.Mapping;

public class MappingProfileTests
{
    [Fact]
    public void Domain_to_Entity_profile_is_valid()
    {
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<DomainEntityMappingProfile>());
        configuration.AssertConfigurationIsValid();
    }

    [Fact]
    public void Domain_to_Dto_profile_is_valid()
    {
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<DomainDtoMappingProfile>());
        configuration.AssertConfigurationIsValid();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.UnitTests --filter MappingProfileTests`
Expected: FAIL — compile error, profiles don't exist yet.

- [ ] **Step 3: Write `DocumentDto`**

```csharp
// src/Lumen.Application/Documents/DocumentDto.cs
namespace Lumen.Application.Documents;

public class DocumentDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
}
```

Deliberately no `TenantId` on the DTO — the client never supplies or sees it directly; it's inferred server-side from the JWT.

- [ ] **Step 4: Write the two mapping profiles**

```csharp
// src/Lumen.Infrastructure/Mapping/DomainEntityMappingProfile.cs
using AutoMapper;
using Lumen.Domain;
using Lumen.Infrastructure.Persistence.Entities;

namespace Lumen.Infrastructure.Mapping;

public class DomainEntityMappingProfile : Profile
{
    public DomainEntityMappingProfile()
    {
        CreateMap<Document, DocumentEntity>().ReverseMap();
        CreateMap<User, UserEntity>().ReverseMap();
        CreateMap<Tenant, TenantEntity>().ReverseMap();
    }
}
```

```csharp
// src/Lumen.Application/Mapping/DomainDtoMappingProfile.cs
using AutoMapper;
using Lumen.Application.Documents;
using Lumen.Domain;

namespace Lumen.Application.Mapping;

public class DomainDtoMappingProfile : Profile
{
    public DomainDtoMappingProfile()
    {
        CreateMap<Document, DocumentDto>().ReverseMap();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Lumen.UnitTests --filter MappingProfileTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Lumen.Infrastructure/Mapping src/Lumen.Application/Documents/DocumentDto.cs src/Lumen.Application/Mapping tests/Lumen.UnitTests/Mapping
git commit -m "feat: add AutoMapper profiles and DocumentDto"
```

---

## Task 6: Repository + UnitOfWork

**Files:**
- Create: `src/Lumen.Application/Abstractions/IRepository.cs`
- Create: `src/Lumen.Application/Abstractions/IUnitOfWork.cs`
- Create: `src/Lumen.Infrastructure/Repositories/DocumentRepository.cs`
- Create: `src/Lumen.Infrastructure/Repositories/UnitOfWork.cs`
- Test: `tests/Lumen.IntegrationTests/Repositories/DocumentRepositoryTests.cs`

**Interfaces:**
- Consumes: `Document` (Domain), `AppDbContext`, `DocumentEntity` (Infrastructure), `DomainEntityMappingProfile` (Task 5).
- Produces: `IRepository<TDomain> { void Add(TDomain), Task<TDomain?> GetByIdAsync(Guid, CancellationToken), Task<List<TDomain>> GetAllAsync(CancellationToken) }`, `IUnitOfWork { IRepository<Document> Documents, Task<int> CompleteAsync(CancellationToken) }`. Every later task that writes/reads a `Document` goes through `IUnitOfWork.Documents`, never `AppDbContext` directly.

- [ ] **Step 1: Write the failing test — this is the test that would have caught the per-call-DbContext bug from the reference project**

```csharp
// tests/Lumen.IntegrationTests/Repositories/DocumentRepositoryTests.cs
using AutoMapper;
using Lumen.Domain;
using Lumen.Infrastructure.Mapping;
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Repositories;
using Lumen.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lumen.IntegrationTests.Repositories;

[Collection("Postgres collection")]
public class DocumentRepositoryTests(PostgresFixture fixture)
{
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<DomainEntityMappingProfile>()).CreateMapper();

    [Fact]
    public async Task Add_then_Complete_actually_persists_the_document()
    {
        var tenantId = Guid.NewGuid();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(fixture.ConnectionString);
        await using var context = new AppDbContext(optionsBuilder.Options, new CurrentTenant { TenantId = tenantId });

        var repository = new DocumentRepository(context, Mapper);
        var unitOfWork = new UnitOfWork(context, repository);

        var document = new Document { Id = Guid.NewGuid(), Title = "Persisted?", Content = "It should be." };
        unitOfWork.Documents.Add(document);
        await unitOfWork.CompleteAsync();

        await using var verifyContext = new AppDbContext(optionsBuilder.Options, new CurrentTenant { TenantId = tenantId });
        var fromDb = await verifyContext.Documents.FirstOrDefaultAsync(d => d.Id == document.Id);

        Assert.NotNull(fromDb);
        Assert.Equal("Persisted?", fromDb!.Title);
        Assert.Equal(tenantId, fromDb.TenantId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.IntegrationTests --filter DocumentRepositoryTests`
Expected: FAIL — compile error, `DocumentRepository`/`UnitOfWork` don't exist yet.

- [ ] **Step 3: Write the repository interfaces**

```csharp
// src/Lumen.Application/Abstractions/IRepository.cs
namespace Lumen.Application.Abstractions;

public interface IRepository<TDomain> where TDomain : class
{
    void Add(TDomain entity);
    Task<TDomain?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<TDomain>> GetAllAsync(CancellationToken ct = default);
}
```

```csharp
// src/Lumen.Application/Abstractions/IUnitOfWork.cs
using Lumen.Domain;

namespace Lumen.Application.Abstractions;

public interface IUnitOfWork
{
    IRepository<Document> Documents { get; }
    Task<int> CompleteAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Write `DocumentRepository` and `UnitOfWork` — sharing ONE injected `AppDbContext`, not a factory**

```csharp
// src/Lumen.Infrastructure/Repositories/DocumentRepository.cs
using AutoMapper;
using Lumen.Application.Abstractions;
using Lumen.Domain;
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lumen.Infrastructure.Repositories;

public class DocumentRepository(AppDbContext context, IMapper mapper) : IRepository<Document>
{
    public void Add(Document entity)
    {
        var dbEntity = mapper.Map<DocumentEntity>(entity);
        context.Documents.Add(dbEntity);
    }

    public async Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var dbEntity = await context.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        return dbEntity is null ? null : mapper.Map<Document>(dbEntity);
    }

    public async Task<List<Document>> GetAllAsync(CancellationToken ct = default)
    {
        var dbEntities = await context.Documents.ToListAsync(ct);
        return mapper.Map<List<Document>>(dbEntities);
    }
}
```

```csharp
// src/Lumen.Infrastructure/Repositories/UnitOfWork.cs
using Lumen.Application.Abstractions;
using Lumen.Domain;
using Lumen.Infrastructure.Persistence;

namespace Lumen.Infrastructure.Repositories;

public class UnitOfWork(AppDbContext context, IRepository<Document> documents) : IUnitOfWork
{
    public IRepository<Document> Documents { get; } = documents;

    public Task<int> CompleteAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
```

Both take the same `AppDbContext` instance via constructor injection. When registered as `AddScoped` in Task 11's DI wiring, DI guarantees `DocumentRepository` and `UnitOfWork` receive the *same* `AppDbContext` instance within one request scope — this is what actually fixes the bug from the reference project (there, `IDbContextFactory.CreateDbContext()` created a fresh, disposed-before-use instance on every call).

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Lumen.IntegrationTests --filter DocumentRepositoryTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Lumen.Application/Abstractions/IRepository.cs src/Lumen.Application/Abstractions/IUnitOfWork.cs src/Lumen.Infrastructure/Repositories tests/Lumen.IntegrationTests/Repositories
git commit -m "feat: add Repository and UnitOfWork over a shared AppDbContext"
```

---

## Task 7: Custom Exceptions & Global Exception Handler

**Files:**
- Create: `src/Lumen.Application/Exceptions/NotFoundException.cs`
- Create: `src/Lumen.Application/Exceptions/ForbiddenException.cs`
- Create: `src/Lumen.Application/Exceptions/ConflictException.cs`
- Create: `src/Lumen.Application/Exceptions/ValidationException.cs`
- Create: `src/Lumen.Application/Exceptions/UnauthorizedException.cs`
- Create: `src/Lumen.Api/ErrorHandling/GlobalExceptionHandler.cs`
- Modify: `src/Lumen.Api/Program.cs`
- Create: `src/Lumen.Api/Controllers/DiagnosticsController.cs` (temporary, deleted in Task 11 once real controllers exist to exercise the handler)
- Test: `tests/Lumen.IntegrationTests/ErrorHandling/GlobalExceptionHandlerTests.cs`

**Interfaces:**
- Produces: five exception types under `Lumen.Application.Exceptions`, each mapped by `GlobalExceptionHandler` to an HTTP status + `ProblemDetails` body. Later tasks (Task 9's login flow, any future validation) throw these types and rely on this mapping — no other exception-to-HTTP-status logic is written anywhere else.

- [ ] **Step 1: Write the exception types**

```csharp
// src/Lumen.Application/Exceptions/NotFoundException.cs
namespace Lumen.Application.Exceptions;

public class NotFoundException(string message) : Exception(message);
```

```csharp
// src/Lumen.Application/Exceptions/ForbiddenException.cs
namespace Lumen.Application.Exceptions;

public class ForbiddenException(string message) : Exception(message);
```

```csharp
// src/Lumen.Application/Exceptions/ConflictException.cs
namespace Lumen.Application.Exceptions;

public class ConflictException(string message) : Exception(message);
```

```csharp
// src/Lumen.Application/Exceptions/ValidationException.cs
namespace Lumen.Application.Exceptions;

public class ValidationException(string message) : Exception(message);
```

```csharp
// src/Lumen.Application/Exceptions/UnauthorizedException.cs
namespace Lumen.Application.Exceptions;

public class UnauthorizedException(string message) : Exception(message);
```

- [ ] **Step 2: Write a temporary diagnostics endpoint to exercise the handler (deleted once `DocumentsController` exists in Task 11)**

```csharp
// src/Lumen.Api/Controllers/DiagnosticsController.cs
using Lumen.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Lumen.Api.Controllers;

[ApiController]
[Route("diagnostics")]
public class DiagnosticsController : ControllerBase
{
    [HttpGet("throw-not-found")]
    public IActionResult ThrowNotFound() => throw new NotFoundException("Diagnostic not-found.");
}
```

- [ ] **Step 3: Write the failing test**

```csharp
// tests/Lumen.IntegrationTests/ErrorHandling/GlobalExceptionHandlerTests.cs
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Lumen.IntegrationTests.ErrorHandling;

[Collection("Postgres collection")]
public class GlobalExceptionHandlerTests(PostgresFixture fixture) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task NotFoundException_maps_to_404_ProblemDetails()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", fixture.ConnectionString);
        });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/diagnostics/throw-not-found");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Diagnostic not-found.", body);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/Lumen.IntegrationTests --filter GlobalExceptionHandlerTests`
Expected: FAIL — `Program` isn't a working, buildable web host with the handler wired yet (or 500 instead of 404 once it builds).

- [ ] **Step 5: Write `GlobalExceptionHandler`**

```csharp
// src/Lumen.Api/ErrorHandling/GlobalExceptionHandler.cs
using Lumen.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Lumen.Api.ErrorHandling;

public class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, exception.Message),
            ForbiddenException => (StatusCodes.Status403Forbidden, exception.Message),
            ConflictException => (StatusCodes.Status409Conflict, exception.Message),
            ValidationException => (StatusCodes.Status400BadRequest, exception.Message),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        httpContext.Response.StatusCode = status;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title },
            cancellationToken);

        return true;
    }
}
```

- [ ] **Step 6: Wire up a minimal `Program.cs` (extended further in later tasks) with the exception handler**

```csharp
// src/Lumen.Api/Program.cs
using Lumen.Api.ErrorHandling;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

public partial class Program;
```

The trailing `public partial class Program;` is required so `WebApplicationFactory<Program>` in the test project can find the entry point type — top-level statements otherwise generate an internal one.

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/Lumen.IntegrationTests --filter GlobalExceptionHandlerTests`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/Lumen.Application/Exceptions src/Lumen.Api/ErrorHandling src/Lumen.Api/Program.cs src/Lumen.Api/Controllers/DiagnosticsController.cs tests/Lumen.IntegrationTests/ErrorHandling
git commit -m "feat: add custom exceptions and global exception handler"
```

---

## Task 8: JWT Token Service & Password Hasher

**Files:**
- Create: `src/Lumen.Infrastructure/Auth/JwtOptions.cs`
- Create: `src/Lumen.Application/Abstractions/IJwtTokenService.cs`
- Create: `src/Lumen.Infrastructure/Auth/JwtTokenService.cs`
- Test: `tests/Lumen.UnitTests/Auth/JwtTokenServiceTests.cs`

**Interfaces:**
- Produces: `IJwtTokenService { string GenerateToken(Guid userId, Guid tenantId, Role role) }`, tokens carrying claims `sub` (user id), `tenant_id`, and a `ClaimTypes.Role`-typed role claim. Task 9's login handler and Task 10's middleware both depend on this exact claim shape.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.UnitTests/Auth/JwtTokenServiceTests.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Lumen.Domain;
using Lumen.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lumen.UnitTests.Auth;

public class JwtTokenServiceTests
{
    [Fact]
    public void GenerateToken_includes_subject_tenant_and_role_claims()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "unit-test-secret-that-is-long-enough-for-hs256",
            Issuer = "Lumen",
            Audience = "Lumen",
            ExpiryMinutes = 60
        });
        var service = new JwtTokenService(options);
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var token = service.GenerateToken(userId, tenantId, Role.Admin);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(userId.ToString(), jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(tenantId.ToString(), jwt.Claims.First(c => c.Type == "tenant_id").Value);
        Assert.Equal(nameof(Role.Admin), jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.UnitTests --filter JwtTokenServiceTests`
Expected: FAIL — compile error, `JwtOptions`/`JwtTokenService` don't exist yet.

- [ ] **Step 3: Write `JwtOptions` and `IJwtTokenService`**

```csharp
// src/Lumen.Infrastructure/Auth/JwtOptions.cs
namespace Lumen.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Secret { get; set; } = default!;
    public string Issuer { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public int ExpiryMinutes { get; set; } = 60;
}
```

```csharp
// src/Lumen.Application/Abstractions/IJwtTokenService.cs
using Lumen.Domain;

namespace Lumen.Application.Abstractions;

public interface IJwtTokenService
{
    string GenerateToken(Guid userId, Guid tenantId, Role role);
}
```

- [ ] **Step 4: Write `JwtTokenService`**

```csharp
// src/Lumen.Infrastructure/Auth/JwtTokenService.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Lumen.Application.Abstractions;
using Lumen.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Lumen.Infrastructure.Auth;

public class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public string GenerateToken(Guid userId, Guid tenantId, Role role)
    {
        Claim[] claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
        ];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Lumen.UnitTests --filter JwtTokenServiceTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Lumen.Infrastructure/Auth src/Lumen.Application/Abstractions/IJwtTokenService.cs tests/Lumen.UnitTests/Auth
git commit -m "feat: add JWT token service"
```

---

## Task 9: User Lookup Service & Login Flow

**Files:**
- Create: `src/Lumen.Application/Abstractions/IUserLookupService.cs`
- Create: `src/Lumen.Infrastructure/Auth/UserLookupService.cs`
- Create: `src/Lumen.Application/Auth/LoginCommand.cs`
- Create: `src/Lumen.Api/Controllers/AuthController.cs`
- Modify: `src/Lumen.Api/Program.cs` (register MediatR, EF, the new services)
- Test: `tests/Lumen.IntegrationTests/Auth/LoginTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 3), `DomainEntityMappingProfile` (Task 5), `IJwtTokenService` (Task 8), `UnauthorizedException` (Task 7).
- Produces: `IUserLookupService { Task<User?> FindByEmailAcrossTenantsAsync(string email, CancellationToken) }` — the **only** place in the codebase allowed to call `.IgnoreQueryFilters()`. `POST /auth/token` returning `{ "token": "<jwt>" }` on success, 401 on bad credentials. Task 10 and Task 13 both authenticate through this endpoint.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.IntegrationTests/Auth/LoginTests.cs
using System.Net;
using System.Net.Http.Json;
using Lumen.Domain;
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Persistence.Entities;
using Lumen.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lumen.IntegrationTests.Auth;

[Collection("Postgres collection")]
public class LoginTests(PostgresFixture fixture)
{
    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", fixture.ConnectionString));

    private async Task<(Guid tenantId, string email, string password)> SeedUserAsync()
    {
        var tenantId = Guid.NewGuid();
        var email = $"{Guid.NewGuid()}@lumen.test";
        const string password = "Correct-Horse-Battery-1";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(fixture.ConnectionString);
        await using var context = new AppDbContext(optionsBuilder.Options, new CurrentTenant { TenantId = tenantId });

        var hasher = new PasswordHasher<User>();
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            Role = Role.Member
        };
        user.PasswordHash = hasher.HashPassword(new User { Id = user.Id, TenantId = tenantId, Email = email, Role = Role.Member }, password);

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return (tenantId, email, password);
    }

    [Fact]
    public async Task Login_with_correct_credentials_returns_a_token()
    {
        var (_, email, password) = await SeedUserAsync();
        var client = CreateFactory().CreateClient();

        var response = await client.PostAsJsonAsync("/auth/token", new { email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.False(string.IsNullOrWhiteSpace(body!["token"]));
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var (_, email, _) = await SeedUserAsync();
        var client = CreateFactory().CreateClient();

        var response = await client.PostAsJsonAsync("/auth/token", new { email, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.IntegrationTests --filter LoginTests`
Expected: FAIL — `/auth/token` doesn't exist yet (404), or compile errors if types are missing.

- [ ] **Step 3: Write `IUserLookupService` and `UserLookupService`**

```csharp
// src/Lumen.Application/Abstractions/IUserLookupService.cs
using Lumen.Domain;

namespace Lumen.Application.Abstractions;

public interface IUserLookupService
{
    Task<User?> FindByEmailAcrossTenantsAsync(string email, CancellationToken ct = default);
}
```

```csharp
// src/Lumen.Infrastructure/Auth/UserLookupService.cs
using AutoMapper;
using Lumen.Application.Abstractions;
using Lumen.Domain;
using Lumen.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lumen.Infrastructure.Auth;

public class UserLookupService(AppDbContext context, IMapper mapper) : IUserLookupService
{
    // The ONLY place in the codebase allowed to bypass the tenant query filter:
    // at login time we don't know the caller's tenant yet — we're finding it out
    // from a globally-unique email. Every other query must go through the filter.
    public async Task<User?> FindByEmailAcrossTenantsAsync(string email, CancellationToken ct = default)
    {
        var entity = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        return entity is null ? null : mapper.Map<User>(entity);
    }
}
```

- [ ] **Step 4: Write the `LoginCommand` and its handler**

```csharp
// src/Lumen.Application/Auth/LoginCommand.cs
using Lumen.Application.Abstractions;
using Lumen.Application.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Lumen.Application.Auth;

public record LoginCommand(string Email, string Password) : IRequest<string>;

public class LoginCommandHandler(
    IUserLookupService userLookup,
    IPasswordHasher<Domain.User> passwordHasher,
    IJwtTokenService jwtTokenService) : IRequestHandler<LoginCommand, string>
{
    public async Task<string> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await userLookup.FindByEmailAcrossTenantsAsync(request.Email, ct)
            ?? throw new UnauthorizedException("Invalid email or password.");

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        return jwtTokenService.GenerateToken(user.Id, user.TenantId, user.Role);
    }
}
```

- [ ] **Step 5: Write `AuthController`**

```csharp
// src/Lumen.Api/Controllers/AuthController.cs
using Lumen.Application.Auth;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Lumen.Api.Controllers;

public record LoginRequest(string Email, string Password);
public record TokenResponse(string Token);

[ApiController]
[Route("auth")]
public class AuthController(IMediator mediator) : ControllerBase
{
    [HttpPost("token")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var token = await mediator.Send(new LoginCommand(request.Email, request.Password), ct);
        return Ok(new TokenResponse(token));
    }
}
```

- [ ] **Step 6: Extend `Program.cs` — register EF, MediatR, AutoMapper, tenancy, repositories, and auth services**

```csharp
// src/Lumen.Api/Program.cs
using Lumen.Api.ErrorHandling;
using Lumen.Application.Abstractions;
using Lumen.Application.Auth;
using Lumen.Application.Mapping;
using Lumen.Domain;
using Lumen.Infrastructure.Auth;
using Lumen.Infrastructure.Mapping;
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Repositories;
using Lumen.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddScoped<CurrentTenant>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());

builder.Services.AddScoped<IRepository<Document>, DocumentRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IUserLookupService, UserLookupService>();
builder.Services.AddSingleton<IPasswordHasher<Lumen.Domain.User>, PasswordHasher<Lumen.Domain.User>>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Services.AddAutoMapper(typeof(DomainEntityMappingProfile), typeof(DomainDtoMappingProfile));

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(LoginCommand).Assembly));

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

public partial class Program;
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/Lumen.IntegrationTests --filter LoginTests`
Expected: PASS (both tests)

- [ ] **Step 8: Re-run the full integration suite to make sure nothing broke**

Run: `dotnet test tests/Lumen.IntegrationTests`
Expected: all PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Lumen.Application/Abstractions/IUserLookupService.cs src/Lumen.Infrastructure/Auth/UserLookupService.cs src/Lumen.Application/Auth src/Lumen.Api/Controllers/AuthController.cs src/Lumen.Api/Program.cs tests/Lumen.IntegrationTests/Auth
git commit -m "feat: add login flow with cross-tenant user lookup and JWT issuance"
```

---

## Task 10: JWT Bearer Auth & Tenant Resolution Middleware

**Files:**
- Create: `src/Lumen.Api/Middleware/TenantResolutionMiddleware.cs`
- Modify: `src/Lumen.Api/Program.cs`
- Test: `tests/Lumen.IntegrationTests/Auth/TenantResolutionTests.cs`

**Interfaces:**
- Consumes: `CurrentTenant` (Task 3), the `tenant_id` claim issued by `JwtTokenService` (Task 8).
- Produces: a request pipeline where an authenticated request's `tenant_id` claim ends up in the scoped `CurrentTenant.TenantId` before any controller/handler runs. Task 11's `DocumentsController` and Task 13's flagship test depend on this being in place.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.IntegrationTests/Auth/TenantResolutionTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Lumen.IntegrationTests.Auth;

[Collection("Postgres collection")]
public class TenantResolutionTests(PostgresFixture fixture)
{
    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", fixture.ConnectionString));

    [Fact]
    public async Task Anonymous_request_to_a_protected_endpoint_returns_401()
    {
        var client = CreateFactory().CreateClient();

        var response = await client.GetAsync("/documents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

This test references `/documents`, which doesn't exist until Task 11 — that's expected; it will 404 (not 401) until then. Write it now anyway since it belongs conceptually with the middleware, and let Task 11 be the one that turns it green.

- [ ] **Step 2: Write `TenantResolutionMiddleware`**

```csharp
// src/Lumen.Api/Middleware/TenantResolutionMiddleware.cs
using Lumen.Infrastructure.Tenancy;

namespace Lumen.Api.Middleware;

public class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CurrentTenant currentTenant)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claim = context.User.FindFirst("tenant_id")?.Value;
            if (Guid.TryParse(claim, out var tenantId))
            {
                currentTenant.TenantId = tenantId;
            }
        }

        await next(context);
    }
}
```

- [ ] **Step 3: Wire JWT bearer authentication and the middleware into `Program.cs`**

```csharp
// src/Lumen.Api/Program.cs
using System.Security.Claims;
using System.Text;
using Lumen.Api.ErrorHandling;
using Lumen.Api.Middleware;
using Lumen.Application.Abstractions;
using Lumen.Application.Auth;
using Lumen.Application.Mapping;
using Lumen.Domain;
using Lumen.Infrastructure.Auth;
using Lumen.Infrastructure.Mapping;
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Repositories;
using Lumen.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;

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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddScoped<CurrentTenant>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());

builder.Services.AddScoped<IRepository<Document>, DocumentRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IUserLookupService, UserLookupService>();
builder.Services.AddSingleton<IPasswordHasher<Lumen.Domain.User>, PasswordHasher<Lumen.Domain.User>>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Services.AddAutoMapper(typeof(DomainEntityMappingProfile), typeof(DomainDtoMappingProfile));

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(LoginCommand).Assembly));

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
```

Middleware order matters: `UseAuthentication()` must run before `TenantResolutionMiddleware` (which reads `context.User`), which must run before `UseAuthorization()`/`MapControllers()` (so `ICurrentTenant` is populated before any controller or `AppDbContext` query filter needs it).

- [ ] **Step 4: Run test to verify current state**

Run: `dotnet test tests/Lumen.IntegrationTests --filter TenantResolutionTests`
Expected: still FAIL (404, since `/documents` doesn't exist) — confirms auth/middleware wiring compiles and runs; Task 11 makes this pass for real.

- [ ] **Step 5: Commit**

```bash
git add src/Lumen.Api/Middleware src/Lumen.Api/Program.cs tests/Lumen.IntegrationTests/Auth/TenantResolutionTests.cs
git commit -m "feat: wire JWT bearer auth and tenant resolution middleware"
```

---

## Task 11: Document CQRS Slice, Controller & Swagger JWT Support

**Files:**
- Create: `src/Lumen.Application/Documents/Commands/CreateDocumentCommand.cs`
- Create: `src/Lumen.Application/Documents/Queries/GetDocumentsQuery.cs`
- Create: `src/Lumen.Application/Documents/Queries/GetDocumentByIdQuery.cs`
- Create: `src/Lumen.Api/Controllers/DocumentsController.cs`
- Delete: `src/Lumen.Api/Controllers/DiagnosticsController.cs` (its job — exercising the exception handler — is now covered by real endpoints)
- Modify: `src/Lumen.Api/Program.cs` (Swagger JWT bearer definition)
- Test: `tests/Lumen.UnitTests/Documents/CreateDocumentCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork` (Task 6), `DocumentDto`, `DomainDtoMappingProfile` (Task 5).
- Produces: `POST /documents`, `GET /documents`, `GET /documents/{id}` — all `[Authorize]`. Task 13's flagship test drives these endpoints directly.

- [ ] **Step 1: Write the failing unit test for the create handler**

```csharp
// tests/Lumen.UnitTests/Documents/CreateDocumentCommandHandlerTests.cs
using AutoMapper;
using Lumen.Application.Abstractions;
using Lumen.Application.Documents.Commands;
using Lumen.Application.Mapping;
using Lumen.Domain;
using Moq;
using Xunit;

namespace Lumen.UnitTests.Documents;

public class CreateDocumentCommandHandlerTests
{
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<DomainDtoMappingProfile>()).CreateMapper();

    [Fact]
    public async Task Handle_adds_the_document_and_completes_the_unit_of_work()
    {
        var repository = new Mock<IRepository<Document>>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Documents).Returns(repository.Object);

        var handler = new CreateDocumentCommandHandler(unitOfWork.Object, Mapper);
        var command = new CreateDocumentCommand("Title", "Content");

        var result = await handler.Handle(command, CancellationToken.None);

        repository.Verify(r => r.Add(It.Is<Document>(d => d.Title == "Title" && d.Content == "Content")), Times.Once);
        unitOfWork.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Title", result.Title);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.UnitTests --filter CreateDocumentCommandHandlerTests`
Expected: FAIL — compile error, `CreateDocumentCommand`/handler don't exist yet.

- [ ] **Step 3: Write the Commands, Queries, and Handlers**

```csharp
// src/Lumen.Application/Documents/Commands/CreateDocumentCommand.cs
using AutoMapper;
using Lumen.Application.Abstractions;
using Lumen.Application.Documents;
using Lumen.Domain;
using MediatR;

namespace Lumen.Application.Documents.Commands;

public record CreateDocumentCommand(string Title, string Content) : IRequest<DocumentDto>;

public class CreateDocumentCommandHandler(IUnitOfWork unitOfWork, IMapper mapper)
    : IRequestHandler<CreateDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> Handle(CreateDocumentCommand request, CancellationToken ct)
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Content = request.Content
        };

        unitOfWork.Documents.Add(document);
        await unitOfWork.CompleteAsync(ct);

        return mapper.Map<DocumentDto>(document);
    }
}
```

```csharp
// src/Lumen.Application/Documents/Queries/GetDocumentsQuery.cs
using AutoMapper;
using Lumen.Application.Abstractions;
using Lumen.Application.Documents;
using MediatR;

namespace Lumen.Application.Documents.Queries;

public record GetDocumentsQuery : IRequest<List<DocumentDto>>;

public class GetDocumentsQueryHandler(IUnitOfWork unitOfWork, IMapper mapper)
    : IRequestHandler<GetDocumentsQuery, List<DocumentDto>>
{
    public async Task<List<DocumentDto>> Handle(GetDocumentsQuery request, CancellationToken ct)
    {
        var documents = await unitOfWork.Documents.GetAllAsync(ct);
        return mapper.Map<List<DocumentDto>>(documents);
    }
}
```

```csharp
// src/Lumen.Application/Documents/Queries/GetDocumentByIdQuery.cs
using AutoMapper;
using Lumen.Application.Abstractions;
using Lumen.Application.Documents;
using MediatR;

namespace Lumen.Application.Documents.Queries;

public record GetDocumentByIdQuery(Guid Id) : IRequest<DocumentDto?>;

public class GetDocumentByIdQueryHandler(IUnitOfWork unitOfWork, IMapper mapper)
    : IRequestHandler<GetDocumentByIdQuery, DocumentDto?>
{
    public async Task<DocumentDto?> Handle(GetDocumentByIdQuery request, CancellationToken ct)
    {
        var document = await unitOfWork.Documents.GetByIdAsync(request.Id, ct);
        return document is null ? null : mapper.Map<DocumentDto>(document);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Lumen.UnitTests --filter CreateDocumentCommandHandlerTests`
Expected: PASS

- [ ] **Step 5: Write `DocumentsController`, remove `DiagnosticsController`**

```csharp
// src/Lumen.Api/Controllers/DocumentsController.cs
using Lumen.Application.Documents;
using Lumen.Application.Documents.Commands;
using Lumen.Application.Documents.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lumen.Api.Controllers;

[ApiController]
[Route("documents")]
[Authorize]
public class DocumentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<DocumentDto>>> GetAll(CancellationToken ct)
        => Ok(await mediator.Send(new GetDocumentsQuery(), ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> GetById(Guid id, CancellationToken ct)
    {
        var document = await mediator.Send(new GetDocumentByIdQuery(id), ct);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpPost]
    public async Task<ActionResult<DocumentDto>> Create(CreateDocumentCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }
}
```

```bash
rm src/Lumen.Api/Controllers/DiagnosticsController.cs
```

- [ ] **Step 6: Add JWT bearer support to Swagger so tokens can be tested from the UI**

In `src/Lumen.Api/Program.cs`, replace `builder.Services.AddSwaggerGen();` with:

```csharp
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste the JWT returned by POST /auth/token."
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
```

- [ ] **Step 7: Re-run the Task 10 middleware test — it should now turn green for real**

Run: `dotnet test tests/Lumen.IntegrationTests --filter TenantResolutionTests`
Expected: PASS — `/documents` now exists and correctly returns 401 for an anonymous request.

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test`
Expected: all PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Lumen.Application/Documents src/Lumen.Api/Controllers/DocumentsController.cs src/Lumen.Api/Program.cs tests/Lumen.UnitTests/Documents
git rm src/Lumen.Api/Controllers/DiagnosticsController.cs
git commit -m "feat: add Document CQRS slice, controller, and Swagger JWT support"
```

---

## Task 12: Seed Data

**Files:**
- Create: `src/Lumen.Infrastructure/Persistence/DbSeeder.cs`
- Modify: `src/Lumen.Api/Program.cs`
- Test: `tests/Lumen.IntegrationTests/Persistence/DbSeederTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 3), `IPasswordHasher<User>` (Task 9).
- Produces: two seeded tenants, one `Admin` user each, one `Document` each — created once, idempotently, in Development. Task 13's flagship test can either rely on this seed data or seed its own; it uses its own for isolation between test runs (see Task 13), but manual/Swagger testing relies on this.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.IntegrationTests/Persistence/DbSeederTests.cs
using Lumen.Domain;
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lumen.IntegrationTests.Persistence;

[Collection("Postgres collection")]
public class DbSeederTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SeedAsync_creates_two_tenants_with_one_admin_and_one_document_each_and_is_idempotent()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(fixture.ConnectionString);
        await using var context = new AppDbContext(optionsBuilder.Options, new CurrentTenant());
        var hasher = new PasswordHasher<User>();

        await DbSeeder.SeedAsync(context, hasher);
        await DbSeeder.SeedAsync(context, hasher); // idempotency check — second call must not duplicate rows

        // Scoped to the seeder's own known rows (by name/email), not raw table counts:
        // this integration test suite shares one Postgres database across every test
        // class (see PostgresFixture), so other tests' unrelated tenants/documents may
        // already be present — asserting "exactly 2 rows total" would be order-dependent
        // and flaky. Asserting "exactly 1 row with this known name" is not.
        await using var verify = new AppDbContext(optionsBuilder.Options, new CurrentTenant());
        Assert.Equal(1, await verify.Tenants.CountAsync(t => t.Name == "Acme Corp"));
        Assert.Equal(1, await verify.Tenants.CountAsync(t => t.Name == "Globex Inc"));
        Assert.Equal(1, await verify.Users.IgnoreQueryFilters().CountAsync(u => u.Email == "admin@acme.test"));
        Assert.Equal(1, await verify.Users.IgnoreQueryFilters().CountAsync(u => u.Email == "admin@globex.test"));
        Assert.Equal(1, await verify.Documents.IgnoreQueryFilters().CountAsync(d => d.Title == "Welcome to Acme"));
        Assert.Equal(1, await verify.Documents.IgnoreQueryFilters().CountAsync(d => d.Title == "Welcome to Globex"));
    }
}
```

Note: this test uses `.IgnoreQueryFilters()` directly in test code (not through `UserLookupService`) purely to assert on row counts across tenants — the "exactly one place in the codebase" constraint from Global Constraints applies to application/production code, not test assertions.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.IntegrationTests --filter DbSeederTests`
Expected: FAIL — compile error, `DbSeeder` doesn't exist yet.

- [ ] **Step 3: Write `DbSeeder`**

```csharp
// src/Lumen.Infrastructure/Persistence/DbSeeder.cs
using Lumen.Domain;
using Lumen.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lumen.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context, IPasswordHasher<User> passwordHasher)
    {
        // Scoped to this seeder's own known tenant, not "any tenant at all" — the
        // database may already contain unrelated tenants (other tests, or a real
        // admin-created tenant in a real environment), and a broader guard would
        // wrongly skip seeding in either case.
        if (await context.Tenants.AnyAsync(t => t.Name == "Acme Corp"))
        {
            return;
        }

        var tenantA = new TenantEntity { Id = Guid.NewGuid(), Name = "Acme Corp" };
        var tenantB = new TenantEntity { Id = Guid.NewGuid(), Name = "Globex Inc" };
        context.Tenants.AddRange(tenantA, tenantB);

        var userA = new UserEntity { Id = Guid.NewGuid(), TenantId = tenantA.Id, Email = "admin@acme.test", Role = Role.Admin };
        userA.PasswordHash = passwordHasher.HashPassword(
            new User { Id = userA.Id, TenantId = tenantA.Id, Email = userA.Email, Role = Role.Admin }, "Passw0rd!");

        var userB = new UserEntity { Id = Guid.NewGuid(), TenantId = tenantB.Id, Email = "admin@globex.test", Role = Role.Admin };
        userB.PasswordHash = passwordHasher.HashPassword(
            new User { Id = userB.Id, TenantId = tenantB.Id, Email = userB.Email, Role = Role.Admin }, "Passw0rd!");

        context.Users.AddRange(userA, userB);

        context.Documents.AddRange(
            new DocumentEntity { Id = Guid.NewGuid(), TenantId = tenantA.Id, Title = "Welcome to Acme", Content = "This is Acme's first document." },
            new DocumentEntity { Id = Guid.NewGuid(), TenantId = tenantB.Id, Title = "Welcome to Globex", Content = "This is Globex's first document." });

        await context.SaveChangesAsync();
    }
}
```

This relies on the Task 3 `SaveChangesAsync` refinement (only stamp `TenantId` when it's `Guid.Empty`) — here every entity already has its `TenantId` set explicitly per-tenant before saving, so the stamping loop leaves them alone.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Lumen.IntegrationTests --filter DbSeederTests`
Expected: PASS

- [ ] **Step 5: Call the seeder at startup in Development**

In `src/Lumen.Api/Program.cs`, after `var app = builder.Build();` and before `app.Run();`, add:

```csharp
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Lumen.Domain.User>>();
    await Lumen.Infrastructure.Persistence.DbSeeder.SeedAsync(db, hasher);
}
```

- [ ] **Step 6: Manually verify against the real local Postgres**

```bash
docker compose up -d
dotnet run --project src/Lumen.Api
```

Open the printed Swagger URL, `POST /auth/token` with `{"email": "admin@acme.test", "password": "Passw0rd!"}`, confirm a token comes back, paste it into Swagger's Authorize dialog, call `GET /documents`, confirm exactly one document ("Welcome to Acme") comes back — not Globex's.

- [ ] **Step 7: Commit**

```bash
git add src/Lumen.Infrastructure/Persistence/DbSeeder.cs src/Lumen.Api/Program.cs tests/Lumen.IntegrationTests/Persistence/DbSeederTests.cs
git commit -m "feat: add development seed data for tenants, users, and documents"
```

---

## Task 13: Flagship Tenant Isolation Integration Test (full HTTP path)

**Files:**
- Create: `tests/Lumen.IntegrationTests/TenantIsolation/DocumentTenantIsolationTests.cs`

**Interfaces:**
- Consumes: everything above — `/auth/token` (Task 9), `/documents` (Task 11), the full auth+tenancy pipeline (Task 10).
- Produces: the canonical tenant-isolation proof for this project — the one `DECISIONS.md` should point to.

This is the test the whole stage was building toward: two tenants, real JWTs, real HTTP calls, asserting one tenant's `GET /documents` never returns the other's data.

- [ ] **Step 1: Write the test**

```csharp
// tests/Lumen.IntegrationTests/TenantIsolation/DocumentTenantIsolationTests.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lumen.Application.Documents;
using Lumen.Domain;
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Persistence.Entities;
using Lumen.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lumen.IntegrationTests.TenantIsolation;

[Collection("Postgres collection")]
public class DocumentTenantIsolationTests(PostgresFixture fixture)
{
    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", fixture.ConnectionString));

    private async Task<(Guid tenantId, string email, string password)> SeedTenantWithUserAndDocumentAsync(string tenantName)
    {
        var tenantId = Guid.NewGuid();
        var email = $"{Guid.NewGuid()}@lumen.test";
        const string password = "Correct-Horse-Battery-1";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(fixture.ConnectionString);
        await using var context = new AppDbContext(optionsBuilder.Options, new CurrentTenant { TenantId = tenantId });

        context.Tenants.Add(new TenantEntity { Id = tenantId, Name = tenantName });

        var hasher = new PasswordHasher<User>();
        var user = new UserEntity { Id = Guid.NewGuid(), TenantId = tenantId, Email = email, Role = Role.Member };
        user.PasswordHash = hasher.HashPassword(new User { Id = user.Id, TenantId = tenantId, Email = email, Role = Role.Member }, password);
        context.Users.Add(user);

        context.Documents.Add(new DocumentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = $"{tenantName} secret document",
            Content = $"Only {tenantName} should ever see this."
        });

        await context.SaveChangesAsync();

        return (tenantId, email, password);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/auth/token", new { email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return body!["token"];
    }

    [Fact]
    public async Task Tenant_A_can_never_see_Tenant_Bs_documents_through_the_API()
    {
        var (_, emailA, passwordA) = await SeedTenantWithUserAndDocumentAsync("Acme");
        var (_, emailB, passwordB) = await SeedTenantWithUserAndDocumentAsync("Globex");

        var client = CreateFactory().CreateClient();

        var tokenA = await LoginAsync(client, emailA, passwordA);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var documentsForA = await client.GetFromJsonAsync<List<DocumentDto>>("/documents");

        Assert.NotNull(documentsForA);
        Assert.Single(documentsForA);
        Assert.Equal("Acme secret document", documentsForA![0].Title);

        var tokenB = await LoginAsync(client, emailB, passwordB);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var documentsForB = await client.GetFromJsonAsync<List<DocumentDto>>("/documents");

        Assert.NotNull(documentsForB);
        Assert.Single(documentsForB);
        Assert.Equal("Globex secret document", documentsForB![0].Title);
    }
}
```

- [ ] **Step 2: Run test to verify it fails first (if run before any earlier task is complete) or passes (if run after Task 12)**

Run: `dotnet test tests/Lumen.IntegrationTests --filter DocumentTenantIsolationTests`
Expected: PASS — by this point in the plan every dependency exists.

- [ ] **Step 3: Run the entire test suite one final time**

Run: `dotnet test`
Expected: all PASS, including every earlier task's tests.

- [ ] **Step 4: Commit**

```bash
git add tests/Lumen.IntegrationTests/TenantIsolation
git commit -m "test: add end-to-end tenant isolation integration test"
```

- [ ] **Step 5: Update `CLAUDE.md`'s "Project status" section**

Change `**Pre-implementation.**` to reflect that Stage 1 is complete and describe how to build/test/run (commands verified in this plan: `dotnet build`, `dotnet test`, `docker compose up -d` + `dotnet run --project src/Lumen.Api`).

- [ ] **Step 6: Write the `DECISIONS.md` entry for this stage**

Follow the template in `DECISIONS.md`. This is a real decision with a real number available from this stage — for example, contrasting "no repository test" vs. "the `DocumentRepositoryTests.Add_then_Complete_actually_persists_the_document` test that would have caught the Task 6 bug pattern," or citing the exact isolation guarantee `DocumentTenantIsolationTests` now proves. Write it once the tests are green and you can point at concrete, run results — not before.

---

## Self-Review

**Spec coverage:** solution structure ✓ (Task 1), Domain/Entity/Dto separation ✓ (Tasks 2, 3, 5), multi-tenancy mechanism incl. login `IgnoreQueryFilters` exception ✓ (Tasks 3, 9), Repository+UnitOfWork corrected from the reference bug ✓ (Task 6), JWT auth ✓ (Tasks 8–10), Minimal Controllers + Swagger with JWT ✓ (Task 11), `IExceptionHandler`-based error handling ✓ (Task 7), docker-compose ✓ (Task 1), seed data ✓ (Task 12), both isolation tests (EF-level in Task 3, full-HTTP flagship in Task 13) ✓, `DECISIONS.md`/`CLAUDE.md` follow-up ✓ (Task 13 final steps).

**Placeholder scan:** no TBD/TODO; every step has real, complete code; every `Run:`/`Expected:` pair is concrete.

**Type consistency:** `IRepository<TDomain>`, `IUnitOfWork.Documents`, `DocumentDto`, `ICurrentTenant`, `ITenantEntity`, `IJwtTokenService.GenerateToken(Guid, Guid, Role)`, `IUserLookupService.FindByEmailAcrossTenantsAsync` are each defined once (Tasks 2–3, 5–9) and referenced identically in every later task — checked across Tasks 6, 9, 11, 13.

---

**Plan complete and saved to `.research/stage-1/plan.md`.** Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
