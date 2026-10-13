# Lumen Stage 2 (Ingest & Chunking) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add file-upload-based document ingestion (HTML and PDF) with a table-aware chunking pipeline on top of Stage 1's `Document` CRUD slice, so that an uploaded SEC filing (or any other document) is normalized into a format-agnostic element model, chunked with token-budgeted overlap while keeping tables atomic, and persisted as tenant-isolated `DocumentChunk` rows.

**Architecture:** `IDocumentReader` (one implementation per input format — `HtmlDocumentReader` via AngleSharp, `PdfDocumentReader` via PdfPig) normalizes any supported file into a `List<DocumentElement>` (`Heading`/`Paragraph`/`Table`). `IChunker` walks that list, using `Microsoft.SemanticKernel.Text.TextChunker` for token-budgeted prose runs and treating `Table` elements as atomic chunks. Raw files are stored on the local filesystem (tenant-scoped subfolders), not as DB blobs. The whole pipeline runs synchronously inside `POST /api/documents/upload`.

**Tech Stack:** .NET 10, AngleSharp, PdfPig, `Microsoft.SemanticKernel.Core` (`TextChunker`), `Microsoft.ML.Tokenizers` (`TiktokenTokenizer`), EF Core 10 + Npgsql, xUnit, Testcontainers.PostgreSql.

**Spec:** `.research/stage-2/design.md`

## Global Constraints

- Target framework: **.NET 10** on every project (unchanged from Stage 1).
- `DocumentChunk` is tenant-owned (`ITenantEntity`), filtered by the same EF global query filter mechanism as `Document` — no new isolation mechanism, no new `.IgnoreQueryFilters()` call site.
- `DocumentChunk` is **one universal entity type** with a `ChunkType` enum (`Prose`/`Table`) — never split into separate `DocumentChunk`/`DocumentTableChunk` entities.
- `Document.RawFilePath` stores a **relative** path only (`{TenantId}/{DocumentId}.{ext}`); raw files live under a configurable root (`DocumentStorage:RootPath` in `appsettings`, default `App_Data/Documents`), never under `wwwroot`.
- File-type detection at upload is **extension-based** (`FileExtensionContentTypeProvider`), never trusts the client-supplied `Content-Type` header.
- No background job/queue infrastructure — the whole ingest pipeline runs synchronously inside the upload request.
- `POST /api/documents` (plain `Title`+`Content` JSON) and `CreateDocumentCommand` are **removed** — upload is the only way to create a `Document` from Stage 2 onward.
- DI-constructed classes (handlers, controllers, services) use traditional constructor + `private readonly` fields, never primary-constructor syntax, per `.research/specification/csharp-project-conventions.md`.
- New cross-file `using` namespaces get promoted to each project's `GlobalUsings.cs` once used in 2+ files in that project, per the same conventions doc — check at the end of each task, don't leave it for a separate pass.

## Pre-implementation corrections (review of 2026-09-18, checked against the real current code)

These **override** anything in the task text below that conflicts with them. Each is tagged with the task(s) it affects.

1. **Never `git commit`** (all tasks) — `CLAUDE.md` forbids it; the user commits by hand. Every "Commit" step below means: stage with `git add <paths>` only, then stop. Never run `git commit`/`git push`.
2. **Integration tests run with** `DOCKER_API_VERSION=1.43 dotnet test ...` (Docker Engine here reports API 1.43) (all integration-test steps).
3. **Tenant FK + real test helpers** (Tasks 2, 3): since Stage 1, `Documents.TenantId → Tenants.Id` is a real FK, so any test that inserts a `DocumentEntity` must first insert a `TenantEntity { Id = tenantId, Name = ... }` in the same context. Build contexts with `TestDbContextFactory.Create(fixture.ConnectionString, tenantId)` (not a hand-rolled `DbContextOptionsBuilder`), and build mappers with `new MapperConfiguration(cfg => cfg.AddProfile<DomainEntityMappingProfile>(), NullLoggerFactory.Instance).CreateMapper()` (AutoMapper 16 requires the logger-factory argument). `tests/Lumen.IntegrationTests/GlobalUsings.cs` already globalizes `Lumen.Domain`, `Lumen.Infrastructure.*`, `Microsoft.EntityFrameworkCore`, `AutoMapper`, `System.Net*` etc. — drop redundant local `using`s in new test files.
4. **Task 3 must also**: (a) update the existing `tests/Lumen.IntegrationTests/Repositories/DocumentRepositoryTests.cs`, which calls `new UnitOfWork(context, repository)` — the constructor gains a third `IDocumentChunkRepository` parameter (construct a `DocumentChunkRepository(context, Mapper)` there); (b) register `services.AddScoped<IDocumentChunkRepository, DocumentChunkRepository>();` in `src/Lumen.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` (`AddLumenServices`), otherwise `IUnitOfWork` can't be resolved at runtime.
5. **Options binding location** (Tasks 4, 8, 11): `AddLumenServices()` has **no** `IConfiguration` parameter. Bind `FileStorageOptions`/`ChunkingOptions` in `src/Lumen.Api/Extensions/WebApplicationBuilderExtensions.cs` inside `Configure()`, next to the existing `builder.Services.Configure<JwtOptions>(...)` line (`builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection(FileStorageOptions.SectionName));` and the same for `ChunkingOptions`). Only the plain `AddScoped<...>` registrations (`IDocumentReaderFactory`, `IChunker`, `IFileStorage`, `IDocumentChunkRepository`) go in `AddLumenServices()`. `Lumen.Api` already globalizes `Lumen.Infrastructure.DependencyInjection`.
6. **Task 7**: `FileExtensionContentTypeProvider` (`Microsoft.AspNetCore.StaticFiles`) is part of the ASP.NET Core shared framework, which the `Lumen.Infrastructure` classlib does not reference yet — add `<ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>` to `src/Lumen.Infrastructure/Lumen.Infrastructure.csproj`.
7. **Task 8**: (a) `Microsoft.SemanticKernel.Text.TextChunker` is marked experimental (`SKEXP0050`) — add `<NoWarn>$(NoWarn);SKEXP0050</NoWarn>` to the `Lumen.Infrastructure` csproj `PropertyGroup`, otherwise CS8305 fails the build; (b) `Microsoft.ML.Tokenizers` 2.x ships vocabularies in separate data packages — also `dotnet add src/Lumen.Infrastructure package Microsoft.ML.Tokenizers.Data.Cl100kBase` and create the tokenizer as `TiktokenTokenizer.CreateForModel("gpt-4")` (cl100k_base = the tokenizer family of the OpenAI/Azure embedding models; verify it constructs, adjust the model name if the installed version rejects it); (c) `TextChunker.SplitPlainTextParagraphs` expects already line-split input — for each prose element first call `TextChunker.SplitPlainTextLines(text, Math.Max(1, _maxTokensPerChunk / 4), CountTokens)` and feed all resulting lines of the run to `SplitPlainTextParagraphs`, otherwise a single long paragraph can exceed `MaxTokensPerChunk`; (d) `CharStart`/`CharEnd` are **approximate** running offsets (overlap and joiners make exact offsets into `Document.Content` impractical) — say so in a one-line comment.
8. **Task 9**: `PdfDocument.Open(stream)` may close/dispose the stream it is given, so do not rely on `request.Content.Position = 0` after reading. Buffer the upload once: `using var buffer = new MemoryStream(); await request.Content.CopyToAsync(buffer, ct); var bytes = buffer.ToArray();` then give the reader `new MemoryStream(bytes)` and give file storage a second `new MemoryStream(bytes)`.
9. **Task 6 fixture**: instead of a hand-made binary PDF, generate the fixture PDF in the test itself with PdfPig's writer (`UglyToad.PdfPig.Writer.PdfDocumentBuilder`: `AddPage`, `AddStandard14Font`/`AddText` — confirm exact signatures against the installed package) so the test is self-contained; lay out one prose line plus a grid of at least 3 rows x 3 columns with wide horizontal gaps. If the builder API can't produce this, fall back to a committed fixture as originally described.
10. **Task 13 fixture source**: the Apple 10-K sample saved by hand is `samples/edgar/aapl-20250927.html` (extension `.html`, not `.htm`); copy it to `tests/Lumen.IntegrationTests/Fixtures/apple-10k-fy2025.htm` (`.htm` and `.html` both map to `text/html`).

---

## Task 1: Domain model — `DocumentChunk`, `ChunkType`, extend `Document`

**Files:**
- Create: `src/Lumen.Domain/ChunkType.cs`
- Create: `src/Lumen.Domain/DocumentChunk.cs`
- Modify: `src/Lumen.Domain/Document.cs`
- Test: `tests/Lumen.UnitTests/Domain/DocumentChunkTests.cs`

**Interfaces:**
- Produces: `Lumen.Domain.ChunkType { Prose, Table }`, `Lumen.Domain.DocumentChunk { Guid Id, Guid DocumentId, Guid TenantId, int ChunkIndex, string Text, int TokenCount, int CharStart, int CharEnd, ChunkType ChunkType }`. `Document` gains `string RawFilePath`, `DateOnly FilingDate`. Every later task's chunk-related code uses exactly these shapes.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.UnitTests/Domain/DocumentChunkTests.cs
using Lumen.Domain;
using Xunit;

namespace Lumen.UnitTests.Domain;

public class DocumentChunkTests
{
    [Fact]
    public void DocumentChunk_holds_the_values_it_was_given()
    {
        var id = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var chunk = new DocumentChunk
        {
            Id = id,
            DocumentId = documentId,
            TenantId = tenantId,
            ChunkIndex = 3,
            Text = "Net sales increased 16% year over year.",
            TokenCount = 42,
            CharStart = 100,
            CharEnd = 250,
            ChunkType = ChunkType.Prose
        };

        Assert.Equal(id, chunk.Id);
        Assert.Equal(documentId, chunk.DocumentId);
        Assert.Equal(tenantId, chunk.TenantId);
        Assert.Equal(3, chunk.ChunkIndex);
        Assert.Equal(42, chunk.TokenCount);
        Assert.Equal(ChunkType.Prose, chunk.ChunkType);
    }

    [Fact]
    public void Document_holds_the_new_raw_file_and_filing_date_fields()
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Title = "Apple 10-K FY2025",
            Content = "cleaned text goes here",
            RawFilePath = "11111111-1111-1111-1111-111111111111/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa.htm",
            FilingDate = new DateOnly(2025, 10, 31)
        };

        Assert.Equal("cleaned text goes here", document.Content);
        Assert.Equal(new DateOnly(2025, 10, 31), document.FilingDate);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.UnitTests --filter DocumentChunkTests`
Expected: FAIL — compile error, `DocumentChunk`/`ChunkType` don't exist yet, `Document` has no `RawFilePath`/`FilingDate`.

- [ ] **Step 3: Write `ChunkType` and `DocumentChunk`**

```csharp
// src/Lumen.Domain/ChunkType.cs
namespace Lumen.Domain;

public enum ChunkType
{
    Prose,
    Table
}
```

```csharp
// src/Lumen.Domain/DocumentChunk.cs
namespace Lumen.Domain;

public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid TenantId { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = default!;
    public int TokenCount { get; set; }
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
    public ChunkType ChunkType { get; set; }
}
```

- [ ] **Step 4: Extend `Document`**

```csharp
// src/Lumen.Domain/Document.cs
namespace Lumen.Domain;

public class Document
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string RawFilePath { get; set; } = default!;
    public DateOnly FilingDate { get; set; }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Lumen.UnitTests --filter DocumentChunkTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Lumen.Domain tests/Lumen.UnitTests/Domain/DocumentChunkTests.cs
git commit -m "feat: add DocumentChunk domain model, extend Document with RawFilePath/FilingDate"
```

---

## Task 2: Persistence — `DocumentChunkEntity`, EF config, `AppDbContext`, migration

**Files:**
- Create: `src/Lumen.Infrastructure/Persistence/Entities/DocumentChunkEntity.cs`
- Create: `src/Lumen.Infrastructure/Persistence/Configurations/DocumentChunkEntityConfiguration.cs`
- Modify: `src/Lumen.Infrastructure/Persistence/Configurations/DocumentEntityConfiguration.cs` (or equivalent — add `RawFilePath`/`FilingDate` columns)
- Modify: `src/Lumen.Infrastructure/Persistence/Entities/DocumentEntity.cs` (add `RawFilePath`, `FilingDate`)
- Modify: `src/Lumen.Infrastructure/Persistence/AppDbContext.cs` (new `DbSet`, query filter)
- Create: migration under `src/Lumen.Infrastructure/Persistence/Migrations/` (generated)
- Test: `tests/Lumen.IntegrationTests/Persistence/DocumentChunkTenantIsolationDbContextTests.cs`

**Interfaces:**
- Consumes: `DocumentChunk`, `ChunkType` (Task 1).
- Produces: `DocumentChunkEntity : ITenantEntity { Guid Id, Guid TenantId, Guid DocumentId, int ChunkIndex, string Text, int TokenCount, int CharStart, int CharEnd, ChunkType ChunkType }`, `AppDbContext.DocumentChunks : DbSet<DocumentChunkEntity>`, filtered by `TenantId == _currentTenant.TenantId` exactly like `DocumentEntity`. Every later task that persists a chunk goes through this DbSet.

- [ ] **Step 1: Write the failing tenant-isolation test for chunks**

```csharp
// tests/Lumen.IntegrationTests/Persistence/DocumentChunkTenantIsolationDbContextTests.cs
using Lumen.Domain;
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Persistence.Entities;
using Lumen.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lumen.IntegrationTests.Persistence;

[Collection("Postgres collection")]
public class DocumentChunkTenantIsolationDbContextTests(PostgresFixture fixture)
{
    private AppDbContext CreateContext(Guid tenantId)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(fixture.ConnectionString);
        return new AppDbContext(optionsBuilder.Options, new CurrentTenant { TenantId = tenantId });
    }

    [Fact]
    public async Task Tenant_A_never_sees_Tenant_Bs_document_chunks()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await using (var seedContext = CreateContext(tenantAId))
        {
            var document = new DocumentEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantAId,
                Title = "A doc",
                Content = "content",
                RawFilePath = "a.htm",
                FilingDate = new DateOnly(2025, 1, 1)
            };
            seedContext.Documents.Add(document);
            seedContext.DocumentChunks.Add(new DocumentChunkEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantAId,
                DocumentId = document.Id,
                ChunkIndex = 0,
                Text = "belongs to A",
                TokenCount = 3,
                CharStart = 0,
                CharEnd = 12,
                ChunkType = ChunkType.Prose
            });
            await seedContext.SaveChangesAsync();
        }

        await using (var seedContext = CreateContext(tenantBId))
        {
            var document = new DocumentEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantBId,
                Title = "B doc",
                Content = "content",
                RawFilePath = "b.htm",
                FilingDate = new DateOnly(2025, 1, 1)
            };
            seedContext.Documents.Add(document);
            seedContext.DocumentChunks.Add(new DocumentChunkEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantBId,
                DocumentId = document.Id,
                ChunkIndex = 0,
                Text = "belongs to B",
                TokenCount = 3,
                CharStart = 0,
                CharEnd = 12,
                ChunkType = ChunkType.Prose
            });
            await seedContext.SaveChangesAsync();
        }

        await using var contextAsTenantA = CreateContext(tenantAId);
        var visibleToA = await contextAsTenantA.DocumentChunks.ToListAsync();

        Assert.Single(visibleToA);
        Assert.All(visibleToA, c => Assert.Equal(tenantAId, c.TenantId));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.IntegrationTests --filter DocumentChunkTenantIsolationDbContextTests`
Expected: FAIL — compile error, `DocumentChunkEntity`/`AppDbContext.DocumentChunks` don't exist yet, `DocumentEntity` has no `RawFilePath`/`FilingDate`.

- [ ] **Step 3: Extend `DocumentEntity`**

```csharp
// src/Lumen.Infrastructure/Persistence/Entities/DocumentEntity.cs
namespace Lumen.Infrastructure.Persistence.Entities;

public class DocumentEntity : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string RawFilePath { get; set; } = default!;
    public DateOnly FilingDate { get; set; }
}
```

- [ ] **Step 4: Write `DocumentChunkEntity`**

```csharp
// src/Lumen.Infrastructure/Persistence/Entities/DocumentChunkEntity.cs
using Lumen.Domain;

namespace Lumen.Infrastructure.Persistence.Entities;

public class DocumentChunkEntity : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = default!;
    public int TokenCount { get; set; }
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
    public ChunkType ChunkType { get; set; }
}
```

- [ ] **Step 5: Write the EF configuration for `DocumentChunkEntity`, extend `DocumentEntityConfiguration`**

```csharp
// src/Lumen.Infrastructure/Persistence/Configurations/DocumentChunkEntityConfiguration.cs
using Lumen.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lumen.Infrastructure.Persistence.Configurations;

public class DocumentChunkEntityConfiguration : IEntityTypeConfiguration<DocumentChunkEntity>
{
    public void Configure(EntityTypeBuilder<DocumentChunkEntity> builder)
    {
        builder.ToTable("DocumentChunks");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Text).IsRequired();
        builder.HasIndex(c => c.TenantId);
        builder.HasIndex(c => new { c.DocumentId, c.ChunkIndex });

        builder.HasOne<DocumentEntity>()
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

Extend the existing `DocumentEntityConfiguration` with:

```csharp
builder.Property(d => d.RawFilePath).IsRequired().HasMaxLength(1000);
builder.Property(d => d.FilingDate).IsRequired();
```

- [ ] **Step 6: Extend `AppDbContext`**

```csharp
// src/Lumen.Infrastructure/Persistence/AppDbContext.cs (relevant additions)
public DbSet<DocumentChunkEntity> DocumentChunks => Set<DocumentChunkEntity>();

// inside OnModelCreating, alongside the existing DocumentEntity filter:
modelBuilder.Entity<DocumentChunkEntity>().HasQueryFilter(c => c.TenantId == _currentTenant.TenantId);
```

`DocumentChunkEntity` also needs to be picked up by the unconditional tenant-stamping loop in `SaveChangesAsync` — no change needed there since that loop already iterates `ChangeTracker.Entries<ITenantEntity>()`, and `DocumentChunkEntity` implements it.

- [ ] **Step 7: Generate the migration**

```bash
dotnet ef migrations add AddDocumentChunksAndRawFileMetadata --project src/Lumen.Infrastructure --startup-project src/Lumen.Api --output-dir Persistence/Migrations
```

Expected: creates a migration with `CreateTable` for `DocumentChunks` and `AddColumn` for `Documents.RawFilePath`/`Documents.FilingDate`.

If existing seeded `Document` rows or tests fail after this migration because `RawFilePath`/`FilingDate` are `NOT NULL` with no default — that's expected; Task 12 updates `DbSeeder` and the tests that create `Document`s directly.

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/Lumen.IntegrationTests --filter DocumentChunkTenantIsolationDbContextTests`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add src/Lumen.Infrastructure tests/Lumen.IntegrationTests/Persistence/DocumentChunkTenantIsolationDbContextTests.cs
git commit -m "feat: add DocumentChunkEntity, extend Document persistence, migrate"
```

---

## Task 3: Repository, `IUnitOfWork`, AutoMapper — `DocumentChunk`

**Files:**
- Create: `src/Lumen.Application/Interfaces/IDocumentChunkRepository.cs`
- Modify: `src/Lumen.Application/Interfaces/IUnitOfWork.cs`
- Create: `src/Lumen.Infrastructure/Repositories/DocumentChunkRepository.cs`
- Modify: `src/Lumen.Infrastructure/Repositories/UnitOfWork.cs`
- Modify: `src/Lumen.Infrastructure/Mapping/DomainEntityMappingProfile.cs`
- Test: `tests/Lumen.IntegrationTests/Repositories/DocumentChunkRepositoryTests.cs`

**Interfaces:**
- Consumes: `DocumentChunk` (Task 1), `DocumentChunkEntity`, `AppDbContext.DocumentChunks` (Task 2).
- Produces: `IDocumentChunkRepository : IRepository<DocumentChunk> { Task<List<DocumentChunk>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default) }`, `IUnitOfWork.Chunks`. Every later task that reads/writes chunks goes through `IUnitOfWork.Chunks`, never `AppDbContext` directly.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.IntegrationTests/Repositories/DocumentChunkRepositoryTests.cs
using AutoMapper;
using Lumen.Domain;
using Lumen.Infrastructure.Mapping;
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Persistence.Entities;
using Lumen.Infrastructure.Repositories;
using Lumen.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lumen.IntegrationTests.Repositories;

[Collection("Postgres collection")]
public class DocumentChunkRepositoryTests(PostgresFixture fixture)
{
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<DomainEntityMappingProfile>()).CreateMapper();

    [Fact]
    public async Task GetByDocumentIdAsync_returns_only_that_documents_chunks_in_order()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var otherDocumentId = Guid.NewGuid();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(fixture.ConnectionString);
        await using var context = new AppDbContext(optionsBuilder.Options, new CurrentTenant { TenantId = tenantId });

        context.Documents.Add(new DocumentEntity { Id = documentId, TenantId = tenantId, Title = "T", Content = "C", RawFilePath = "x.htm", FilingDate = new DateOnly(2025, 1, 1) });
        context.Documents.Add(new DocumentEntity { Id = otherDocumentId, TenantId = tenantId, Title = "T2", Content = "C2", RawFilePath = "y.htm", FilingDate = new DateOnly(2025, 1, 1) });
        await context.SaveChangesAsync();

        var repository = new DocumentChunkRepository(context, Mapper);
        var unitOfWork = new UnitOfWork(context, new DocumentRepository(context, Mapper), repository);

        unitOfWork.Chunks.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = 1, Text = "second", TokenCount = 1, CharStart = 10, CharEnd = 16, ChunkType = ChunkType.Prose });
        unitOfWork.Chunks.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = 0, Text = "first", TokenCount = 1, CharStart = 0, CharEnd = 5, ChunkType = ChunkType.Prose });
        unitOfWork.Chunks.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = otherDocumentId, ChunkIndex = 0, Text = "other doc", TokenCount = 2, CharStart = 0, CharEnd = 9, ChunkType = ChunkType.Prose });
        await unitOfWork.CompleteAsync();

        var chunks = await unitOfWork.Chunks.GetByDocumentIdAsync(documentId);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("first", chunks[0].Text);
        Assert.Equal("second", chunks[1].Text);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.IntegrationTests --filter DocumentChunkRepositoryTests`
Expected: FAIL — compile error, `DocumentChunkRepository`/`IUnitOfWork.Chunks` don't exist yet.

- [ ] **Step 3: Write `IDocumentChunkRepository`, extend `IUnitOfWork`**

```csharp
// src/Lumen.Application/Interfaces/IDocumentChunkRepository.cs
using Lumen.Domain;

namespace Lumen.Application.Interfaces;

public interface IDocumentChunkRepository : IRepository<DocumentChunk>
{
    Task<List<DocumentChunk>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
}
```

```csharp
// src/Lumen.Application/Interfaces/IUnitOfWork.cs
using Lumen.Domain;

namespace Lumen.Application.Interfaces;

public interface IUnitOfWork
{
    IRepository<Document> Documents { get; }
    IDocumentChunkRepository Chunks { get; }
    Task<int> CompleteAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Write `DocumentChunkRepository`, extend `UnitOfWork`**

```csharp
// src/Lumen.Infrastructure/Repositories/DocumentChunkRepository.cs
using AutoMapper;
using Lumen.Application.Interfaces;
using Lumen.Domain;
using Lumen.Infrastructure.Persistence;
using Lumen.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lumen.Infrastructure.Repositories;

public class DocumentChunkRepository : IDocumentChunkRepository
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public DocumentChunkRepository(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public void Add(DocumentChunk entity)
    {
        var dbEntity = _mapper.Map<DocumentChunkEntity>(entity);
        _context.DocumentChunks.Add(dbEntity);
    }

    public async Task<DocumentChunk?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var dbEntity = await _context.DocumentChunks.FirstOrDefaultAsync(c => c.Id == id, ct);
        return dbEntity is null ? null : _mapper.Map<DocumentChunk>(dbEntity);
    }

    public async Task<List<DocumentChunk>> GetAllAsync(CancellationToken ct = default)
    {
        var dbEntities = await _context.DocumentChunks.ToListAsync(ct);
        return _mapper.Map<List<DocumentChunk>>(dbEntities);
    }

    public async Task<List<DocumentChunk>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default)
    {
        var dbEntities = await _context.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);
        return _mapper.Map<List<DocumentChunk>>(dbEntities);
    }
}
```

```csharp
// src/Lumen.Infrastructure/Repositories/UnitOfWork.cs
using Lumen.Application.Interfaces;
using Lumen.Domain;
using Lumen.Infrastructure.Persistence;

namespace Lumen.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context, IRepository<Document> documents, IDocumentChunkRepository chunks)
    {
        _context = context;
        Documents = documents;
        Chunks = chunks;
    }

    public IRepository<Document> Documents { get; }
    public IDocumentChunkRepository Chunks { get; }

    public Task<int> CompleteAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
```

Note: `Documents` (`Lumen.Domain`) is ambiguous with `AppDbContext.Documents`/`DocumentChunks` DbSet properties only by name similarity, not by namespace — no `using` conflict, since `IUnitOfWork.Documents`/`.Chunks` are properties on a different type. If `DocumentRepository`'s constructor changed shape in Stage 1's post-completion pass, keep whatever its actual current signature is — `UnitOfWork`'s constructor above assumes `DocumentRepository(AppDbContext, IMapper)` per Stage 1.

- [ ] **Step 5: Add the chunk map to `DomainEntityMappingProfile`**

```csharp
// src/Lumen.Infrastructure/Mapping/DomainEntityMappingProfile.cs (add inside the constructor)
CreateMap<DocumentChunk, DocumentChunkEntity>().ReverseMap();
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Lumen.IntegrationTests --filter DocumentChunkRepositoryTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Lumen.Application/Interfaces src/Lumen.Infrastructure/Repositories src/Lumen.Infrastructure/Mapping tests/Lumen.IntegrationTests/Repositories
git commit -m "feat: add DocumentChunkRepository and extend UnitOfWork"
```

---

## Task 4: File storage — `IFileStorage` / `LocalFileStorage`

**Files:**
- Create: `src/Lumen.Application/Interfaces/IFileStorage.cs`
- Create: `src/Lumen.Infrastructure/DependencyInjection/FileStorageOptions.cs`
- Create: `src/Lumen.Infrastructure/Storage/LocalFileStorage.cs`
- Test: `tests/Lumen.UnitTests/Storage/LocalFileStorageTests.cs`

**Interfaces:**
- Produces: `IFileStorage { Task<string> SaveAsync(Guid tenantId, Guid documentId, string extension, Stream content, CancellationToken ct = default); Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default); }`. Task 9's upload handler is the only consumer for Stage 2.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.UnitTests/Storage/LocalFileStorageTests.cs
using System.Text;
using Lumen.Infrastructure.DependencyInjection;
using Lumen.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lumen.UnitTests.Storage;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lumen-tests-" + Guid.NewGuid());

    [Fact]
    public async Task SaveAsync_writes_the_file_under_tenant_and_document_scoped_path()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("<html>hi</html>"));

        var relativePath = await storage.SaveAsync(tenantId, documentId, ".htm", content);

        Assert.Equal($"{tenantId}/{documentId}.htm", relativePath);
        Assert.True(File.Exists(Path.Combine(_root, tenantId.ToString(), $"{documentId}.htm")));
    }

    [Fact]
    public async Task OpenReadAsync_reads_back_what_was_saved()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("<html>hi</html>"));
        var relativePath = await storage.SaveAsync(tenantId, documentId, ".htm", content);

        await using var readBack = await storage.OpenReadAsync(relativePath);
        using var reader = new StreamReader(readBack);
        var text = await reader.ReadToEndAsync();

        Assert.Equal("<html>hi</html>", text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.UnitTests --filter LocalFileStorageTests`
Expected: FAIL — compile error, `LocalFileStorage`/`FileStorageOptions` don't exist yet.

- [ ] **Step 3: Write `IFileStorage` and `FileStorageOptions`**

```csharp
// src/Lumen.Application/Interfaces/IFileStorage.cs
namespace Lumen.Application.Interfaces;

public interface IFileStorage
{
    Task<string> SaveAsync(Guid tenantId, Guid documentId, string extension, Stream content, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default);
}
```

```csharp
// src/Lumen.Infrastructure/DependencyInjection/FileStorageOptions.cs
namespace Lumen.Infrastructure.DependencyInjection;

public class FileStorageOptions
{
    public const string SectionName = "DocumentStorage";
    public string RootPath { get; set; } = "App_Data/Documents";
}
```

- [ ] **Step 4: Write `LocalFileStorage`**

```csharp
// src/Lumen.Infrastructure/Storage/LocalFileStorage.cs
using Lumen.Application.Interfaces;
using Lumen.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lumen.Infrastructure.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(IOptions<FileStorageOptions> options)
    {
        _rootPath = options.Value.RootPath;
    }

    public async Task<string> SaveAsync(Guid tenantId, Guid documentId, string extension, Stream content, CancellationToken ct = default)
    {
        var relativePath = $"{tenantId}/{documentId}{extension}";
        var absolutePath = Path.Combine(_rootPath, tenantId.ToString(), $"{documentId}{extension}");

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using var fileStream = File.Create(absolutePath);
        await content.CopyToAsync(fileStream, ct);

        return relativePath;
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var absolutePath = Path.Combine(_rootPath, relativePath);
        Stream stream = File.OpenRead(absolutePath);
        return Task.FromResult(stream);
    }
}
```

`relativePath` is always built from the caller-supplied `tenantId`/`documentId` parameters, never from user-controlled free text — callers (Task 9's handler) must pass the authenticated caller's own `TenantId`, never anything read from the request body, to preserve the filesystem-level tenant isolation the design calls for.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Lumen.UnitTests --filter LocalFileStorageTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Lumen.Application/Interfaces/IFileStorage.cs src/Lumen.Infrastructure/DependencyInjection/FileStorageOptions.cs src/Lumen.Infrastructure/Storage tests/Lumen.UnitTests/Storage
git commit -m "feat: add local filesystem-backed IFileStorage"
```

---

## Task 5: Ingestion model + `HtmlDocumentReader`

**Files:**
- Create: `src/Lumen.Application/Ingestion/ElementType.cs`
- Create: `src/Lumen.Application/Ingestion/DocumentElement.cs`
- Create: `src/Lumen.Application/Interfaces/IDocumentReader.cs`
- Create: `src/Lumen.Infrastructure/DocumentReading/HtmlDocumentReader.cs`
- Test: `tests/Lumen.UnitTests/DocumentReading/HtmlDocumentReaderTests.cs`

**Interfaces:**
- Produces: `ElementType { Heading, Paragraph, Table }`, `DocumentElement { ElementType Type, string Text }`, `IDocumentReader { Task<List<DocumentElement>> ReadAsync(Stream fileStream, CancellationToken ct = default) }`, `HtmlDocumentReader : IDocumentReader`. Task 6 (`PdfDocumentReader`), Task 7 (factory), and Task 8 (chunker) all consume exactly this shape.

- [ ] **Step 1: Add the AngleSharp package**

```bash
dotnet add src/Lumen.Infrastructure package AngleSharp
```

- [ ] **Step 2: Write the failing test**

```csharp
// tests/Lumen.UnitTests/DocumentReading/HtmlDocumentReaderTests.cs
using System.Text;
using Lumen.Application.Ingestion;
using Lumen.Infrastructure.DocumentReading;
using Xunit;

namespace Lumen.UnitTests.DocumentReading;

public class HtmlDocumentReaderTests
{
    [Fact]
    public async Task ReadAsync_classifies_heading_paragraph_and_table_separately()
    {
        const string html = """
            <html><body>
              <h1>Item 7. MD&amp;A</h1>
              <p>Net sales increased due to strength in Services.</p>
              <div><span>Extra prose wrapped in a div, not a p tag.</span></div>
              <table>
                <tr><td>Segment</td><td>Net Sales</td></tr>
                <tr><td>Americas</td><td>45781</td></tr>
              </table>
            </body></html>
            """;

        var reader = new HtmlDocumentReader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var elements = await reader.ReadAsync(stream);

        Assert.Equal(ElementType.Heading, elements[0].Type);
        Assert.Contains("MD&A", elements[0].Text);
        Assert.Equal(ElementType.Paragraph, elements[1].Type);
        Assert.Equal(ElementType.Paragraph, elements[2].Type);
        Assert.Contains("Extra prose", elements[2].Text);
        Assert.Equal(ElementType.Table, elements[3].Type);
        Assert.Contains("Americas", elements[3].Text);
        Assert.Contains("45781", elements[3].Text);
    }

    [Fact]
    public async Task ReadAsync_ignores_empty_spacer_divs()
    {
        const string html = "<html><body><div style=\"margin-top:9pt\"><span><br/></span></div><p>Real content.</p></body></html>";
        var reader = new HtmlDocumentReader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var elements = await reader.ReadAsync(stream);

        Assert.Single(elements);
        Assert.Equal("Real content.", elements[0].Text);
    }

    [Fact]
    public async Task ReadAsync_does_not_flatten_a_div_that_contains_a_nested_table()
    {
        const string html = "<html><body><div><table><tr><td>A</td><td>B</td></tr></table></div></body></html>";
        var reader = new HtmlDocumentReader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var elements = await reader.ReadAsync(stream);

        Assert.Single(elements);
        Assert.Equal(ElementType.Table, elements[0].Type);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Lumen.UnitTests --filter HtmlDocumentReaderTests`
Expected: FAIL — compile error, none of the types exist yet.

- [ ] **Step 4: Write `ElementType`, `DocumentElement`, `IDocumentReader`**

```csharp
// src/Lumen.Application/Ingestion/ElementType.cs
namespace Lumen.Application.Ingestion;

public enum ElementType
{
    Heading,
    Paragraph,
    Table
}
```

```csharp
// src/Lumen.Application/Ingestion/DocumentElement.cs
namespace Lumen.Application.Ingestion;

public class DocumentElement
{
    public ElementType Type { get; set; }
    public string Text { get; set; } = default!;
}
```

```csharp
// src/Lumen.Application/Interfaces/IDocumentReader.cs
using Lumen.Application.Ingestion;

namespace Lumen.Application.Interfaces;

public interface IDocumentReader
{
    Task<List<DocumentElement>> ReadAsync(Stream fileStream, CancellationToken ct = default);
}
```

- [ ] **Step 5: Write `HtmlDocumentReader`**

```csharp
// src/Lumen.Infrastructure/DocumentReading/HtmlDocumentReader.cs
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Lumen.Application.Ingestion;
using Lumen.Application.Interfaces;

namespace Lumen.Infrastructure.DocumentReading;

public class HtmlDocumentReader : IDocumentReader
{
    public async Task<List<DocumentElement>> ReadAsync(Stream fileStream, CancellationToken ct = default)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(fileStream), ct);

        var elements = new List<DocumentElement>();
        if (document.Body is not null)
            WalkNode(document.Body, elements);

        return elements;
    }

    private static void WalkNode(INode node, List<DocumentElement> elements)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is IElement element)
            {
                var tag = element.TagName.ToLowerInvariant();

                if (tag == "table")
                {
                    var tableText = ExtractTableText(element);
                    if (!string.IsNullOrWhiteSpace(tableText))
                        elements.Add(new DocumentElement { Type = ElementType.Table, Text = tableText });
                    continue;
                }

                if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
                {
                    var headingText = CleanText(element.TextContent);
                    if (!string.IsNullOrWhiteSpace(headingText))
                        elements.Add(new DocumentElement { Type = ElementType.Heading, Text = headingText });
                    continue;
                }

                if (tag is "p" or "div" && !element.QuerySelectorAll("table").Any())
                {
                    var paragraphText = CleanText(element.TextContent);
                    if (!string.IsNullOrWhiteSpace(paragraphText))
                    {
                        elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = paragraphText });
                        continue;
                    }
                }
            }

            WalkNode(child, elements);
        }
    }

    private static string ExtractTableText(IElement table)
    {
        var lines = new List<string>();
        foreach (var row in table.QuerySelectorAll("tr"))
        {
            var cells = row.QuerySelectorAll("td, th")
                .Select(cell => CleanText(cell.TextContent))
                .Where(text => text.Length > 0);
            var line = string.Join(" | ", cells);
            if (line.Length > 0)
                lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    private static string CleanText(string raw)
    {
        var decoded = System.Net.WebUtility.HtmlDecode(raw);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
```

Note the `tag is "p" or "div" && !element.QuerySelectorAll("table").Any()` condition: a `div`/`p` containing a nested `<table>` is *not* flattened into one paragraph — the walker falls through to recurse into its children instead, so the nested table still gets discovered and emitted as its own `Table` element (this is exactly what `ReadAsync_does_not_flatten_a_div_that_contains_a_nested_table` checks). This handles the real pattern observed in both Apple's and JPMorgan's filings during design: text-bearing `<div>` elements with no `<p>` wrapper.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Lumen.UnitTests --filter HtmlDocumentReaderTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Lumen.Application/Ingestion src/Lumen.Application/Interfaces/IDocumentReader.cs src/Lumen.Infrastructure/DocumentReading tests/Lumen.UnitTests/DocumentReading
git commit -m "feat: add DocumentElement model and AngleSharp-based HtmlDocumentReader"
```

---

## Task 6: `PdfDocumentReader`

**Files:**
- Create: `src/Lumen.Infrastructure/DocumentReading/PdfDocumentReader.cs`
- Create (binary fixture, not code): `tests/Lumen.UnitTests/Fixtures/simple-table.pdf`
- Test: `tests/Lumen.UnitTests/DocumentReading/PdfDocumentReaderTests.cs`

**Interfaces:**
- Consumes: `DocumentElement`, `ElementType`, `IDocumentReader` (Task 5).
- Produces: `PdfDocumentReader : IDocumentReader`. Task 7 (factory) consumes this.

**Known risk (per `.research/stage-2/design.md`):** PdfPig has no built-in table-detection API. The row/column-clustering heuristic below is a first-pass implementation, not a verified reference technique — flag it for revisiting once it's run against a real-world PDF, the same way `MaxTokensPerChunk`/`OverlapTokens` are flagged for empirical retuning.

- [ ] **Step 1: Add the PdfPig package**

```bash
dotnet add src/Lumen.Infrastructure package PdfPig
```

- [ ] **Step 2: Create the fixture PDF**

Create a one-page PDF containing one short paragraph of prose followed by a small 2-column, 3-row numeric table, and save it as `tests/Lumen.UnitTests/Fixtures/simple-table.pdf`. Any tool works (export a short Word/Google Doc page, or `wkhtmltopdf`/LibreOffice on a small HTML file) — the exact tool doesn't matter, only that the resulting PDF has one paragraph of normal running text and one grid of numbers/labels with clear column gaps.

In the `.csproj` for `Lumen.UnitTests`, make sure the fixture is copied to the test output directory:

```xml
<ItemGroup>
  <None Include="Fixtures\simple-table.pdf">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 3: Write the failing test**

```csharp
// tests/Lumen.UnitTests/DocumentReading/PdfDocumentReaderTests.cs
using Lumen.Application.Ingestion;
using Lumen.Infrastructure.DocumentReading;
using Xunit;

namespace Lumen.UnitTests.DocumentReading;

public class PdfDocumentReaderTests
{
    [Fact]
    public async Task ReadAsync_separates_prose_from_the_table_grid()
    {
        var reader = new PdfDocumentReader();
        await using var stream = File.OpenRead(Path.Combine("Fixtures", "simple-table.pdf"));

        var elements = await reader.ReadAsync(stream);

        Assert.Contains(elements, e => e.Type == ElementType.Paragraph);
        Assert.Contains(elements, e => e.Type == ElementType.Table);
    }
}
```

If the fixture's actual text/table content differs from a generic assumption, adjust the assertions to match what the fixture actually contains — the important behavior under test is that *some* element comes back as `Paragraph` and *some* other comes back as `Table`, not the exact wording.

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/Lumen.UnitTests --filter PdfDocumentReaderTests`
Expected: FAIL — compile error, `PdfDocumentReader` doesn't exist yet.

- [ ] **Step 5: Write `PdfDocumentReader`**

```csharp
// src/Lumen.Infrastructure/DocumentReading/PdfDocumentReader.cs
using Lumen.Application.Ingestion;
using Lumen.Application.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Lumen.Infrastructure.DocumentReading;

public class PdfDocumentReader : IDocumentReader
{
    public Task<List<DocumentElement>> ReadAsync(Stream fileStream, CancellationToken ct = default)
    {
        var elements = new List<DocumentElement>();
        using var document = PdfDocument.Open(fileStream);

        foreach (var page in document.GetPages())
        {
            var words = page.GetWords().ToList();
            if (words.Count == 0)
                continue;

            var lines = GroupWordsIntoLines(words);
            var tableLines = new List<List<Word>>();

            foreach (var line in lines)
            {
                if (LooksLikeTableRow(line))
                {
                    tableLines.Add(line);
                    continue;
                }

                var text = string.Join(" ", line.Select(w => w.Text));
                if (!string.IsNullOrWhiteSpace(text))
                    elements.Add(new DocumentElement { Type = ElementType.Paragraph, Text = text });
            }

            if (tableLines.Count > 0)
            {
                var tableText = string.Join("\n", tableLines.Select(line => string.Join(" | ", line.Select(w => w.Text))));
                elements.Add(new DocumentElement { Type = ElementType.Table, Text = tableText });
            }
        }

        return Task.FromResult(elements);
    }

    // First-pass heuristic: group words whose bounding boxes share the same
    // vertical position (within a small tolerance) into a "line," reading
    // top-to-bottom, left-to-right. There is no verified reference
    // implementation for this in the .NET ecosystem (see design.md) — revisit
    // against real PDFs before trusting this on anything but simple layouts.
    private static List<List<Word>> GroupWordsIntoLines(List<Word> words)
    {
        const double lineTolerance = 2.0;
        var lines = new List<List<Word>>();

        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Top))
        {
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].BoundingBox.Top - word.BoundingBox.Top) < lineTolerance);
            if (line is null)
            {
                line = new List<Word>();
                lines.Add(line);
            }
            line.Add(word);
        }

        foreach (var line in lines)
            line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));

        return lines;
    }

    // Heuristic: 3+ words on one line with at least two gaps wider than 1.5x
    // the line's average word width suggests column-aligned cells rather than
    // flowing prose (flowing text has fairly uniform inter-word spacing).
    private static bool LooksLikeTableRow(List<Word> line)
    {
        if (line.Count < 3)
            return false;

        var gaps = new List<double>();
        for (var i = 1; i < line.Count; i++)
            gaps.Add(line[i].BoundingBox.Left - line[i - 1].BoundingBox.Right);

        var averageWordWidth = line.Average(w => w.BoundingBox.Width);
        return gaps.Count(gap => gap > averageWordWidth * 1.5) >= 2;
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Lumen.UnitTests --filter PdfDocumentReaderTests`
Expected: PASS. If it doesn't, the fixture's real word spacing may not match the `1.5x average word width` heuristic threshold — inspect the fixture's actual `Word.BoundingBox` values and adjust the threshold, this is exactly the kind of constant the design flagged as needing real-world calibration.

- [ ] **Step 7: Commit**

```bash
git add src/Lumen.Infrastructure/DocumentReading/PdfDocumentReader.cs tests/Lumen.UnitTests/DocumentReading/PdfDocumentReaderTests.cs tests/Lumen.UnitTests/Fixtures/simple-table.pdf tests/Lumen.UnitTests/Lumen.UnitTests.csproj
git commit -m "feat: add PdfPig-based PdfDocumentReader with heuristic table detection"
```

---

## Task 7: `IDocumentReaderFactory`

**Files:**
- Create: `src/Lumen.Application/Interfaces/IDocumentReaderFactory.cs`
- Create: `src/Lumen.Infrastructure/DocumentReading/DocumentReaderFactory.cs`
- Test: `tests/Lumen.UnitTests/DocumentReading/DocumentReaderFactoryTests.cs`

**Interfaces:**
- Consumes: `HtmlDocumentReader` (Task 5), `PdfDocumentReader` (Task 6).
- Produces: `IDocumentReaderFactory { IDocumentReader GetReader(string fileName) }`. Task 9's upload handler consumes this.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.UnitTests/DocumentReading/DocumentReaderFactoryTests.cs
using Lumen.Application.Exceptions;
using Lumen.Infrastructure.DocumentReading;
using Xunit;

namespace Lumen.UnitTests.DocumentReading;

public class DocumentReaderFactoryTests
{
    [Theory]
    [InlineData("apple-10k.htm", typeof(HtmlDocumentReader))]
    [InlineData("apple-10k.html", typeof(HtmlDocumentReader))]
    [InlineData("prospectus.pdf", typeof(PdfDocumentReader))]
    public void GetReader_picks_the_reader_matching_the_file_extension(string fileName, Type expectedReaderType)
    {
        var factory = new DocumentReaderFactory();

        var reader = factory.GetReader(fileName);

        Assert.IsType(expectedReaderType, reader);
    }

    [Fact]
    public void GetReader_throws_ValidationException_for_an_unsupported_extension()
    {
        var factory = new DocumentReaderFactory();

        Assert.Throws<ValidationException>(() => factory.GetReader("notes.docx"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.UnitTests --filter DocumentReaderFactoryTests`
Expected: FAIL — compile error, `DocumentReaderFactory` doesn't exist yet.

- [ ] **Step 3: Write `IDocumentReaderFactory`**

```csharp
// src/Lumen.Application/Interfaces/IDocumentReaderFactory.cs
namespace Lumen.Application.Interfaces;

public interface IDocumentReaderFactory
{
    IDocumentReader GetReader(string fileName);
}
```

- [ ] **Step 4: Write `DocumentReaderFactory`**

```csharp
// src/Lumen.Infrastructure/DocumentReading/DocumentReaderFactory.cs
using Lumen.Application.Exceptions;
using Lumen.Application.Interfaces;
using Microsoft.AspNetCore.StaticFiles;

namespace Lumen.Infrastructure.DocumentReading;

public class DocumentReaderFactory : IDocumentReaderFactory
{
    private static readonly FileExtensionContentTypeProvider MimeTypeProvider = new();

    public IDocumentReader GetReader(string fileName)
    {
        if (!MimeTypeProvider.TryGetContentType(fileName, out var mimeType))
            throw new ValidationException($"Unsupported file type: '{fileName}'.");

        return mimeType switch
        {
            "text/html" => new HtmlDocumentReader(),
            "application/pdf" => new PdfDocumentReader(),
            _ => throw new ValidationException($"Unsupported file type: '{fileName}' ({mimeType}).")
        };
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Lumen.UnitTests --filter DocumentReaderFactoryTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Lumen.Application/Interfaces/IDocumentReaderFactory.cs src/Lumen.Infrastructure/DocumentReading/DocumentReaderFactory.cs tests/Lumen.UnitTests/DocumentReading/DocumentReaderFactoryTests.cs
git commit -m "feat: add DocumentReaderFactory (extension-based reader selection)"
```

---

## Task 8: `IChunker` — token-budgeted, table-atomic chunking

**Files:**
- Create: `src/Lumen.Application/Interfaces/IChunker.cs`
- Create: `src/Lumen.Infrastructure/DependencyInjection/ChunkingOptions.cs`
- Create: `src/Lumen.Infrastructure/Chunking/Chunker.cs`
- Test: `tests/Lumen.UnitTests/Chunking/ChunkerTests.cs`

**Interfaces:**
- Consumes: `DocumentElement`, `ElementType` (Task 5), `DocumentChunk`, `ChunkType` (Task 1).
- Produces: `IChunker { List<DocumentChunk> Chunk(Guid documentId, List<DocumentElement> elements) }`. Task 9's upload handler consumes this.

- [ ] **Step 1: Add the SemanticKernel and tokenizer packages**

```bash
dotnet add src/Lumen.Infrastructure package Microsoft.SemanticKernel.Core
dotnet add src/Lumen.Infrastructure package Microsoft.ML.Tokenizers
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Lumen.UnitTests/Chunking/ChunkerTests.cs
using Lumen.Application.Ingestion;
using Lumen.Domain;
using Lumen.Infrastructure.Chunking;
using Lumen.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lumen.UnitTests.Chunking;

public class ChunkerTests
{
    private static Chunker CreateChunker(int maxTokens = 250, int overlapTokens = 60) =>
        new(Options.Create(new ChunkingOptions { MaxTokensPerChunk = maxTokens, OverlapTokens = overlapTokens }));

    [Fact]
    public void Chunk_produces_prose_chunks_for_a_short_document()
    {
        var chunker = CreateChunker();
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Heading, Text = "Item 7. MD&A" },
            new() { Type = ElementType.Paragraph, Text = "Net sales increased due to strength in Services and Mac." }
        };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal(ChunkType.Prose, c.ChunkType));
        Assert.All(chunks, c => Assert.True(c.TokenCount > 0));
    }

    [Fact]
    public void Chunk_never_merges_a_table_with_surrounding_prose()
    {
        var chunker = CreateChunker();
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Paragraph, Text = "Segment net sales for the quarter were as follows." },
            new() { Type = ElementType.Table, Text = "Americas | 45781\nEurope | 29395" },
            new() { Type = ElementType.Paragraph, Text = "Americas net sales increased primarily due to iPhone." }
        };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        var tableChunks = chunks.Where(c => c.ChunkType == ChunkType.Table).ToList();
        Assert.Single(tableChunks);
        Assert.Contains("45781", tableChunks[0].Text);
        Assert.DoesNotContain("Segment net sales", tableChunks[0].Text);
        Assert.DoesNotContain("iPhone", tableChunks[0].Text);
    }

    [Fact]
    public void Chunk_assigns_sequential_ChunkIndex_values()
    {
        var chunker = CreateChunker();
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Paragraph, Text = "First paragraph." },
            new() { Type = ElementType.Table, Text = "A | 1" },
            new() { Type = ElementType.Paragraph, Text = "Last paragraph." }
        };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));
    }

    [Fact]
    public void Chunk_splits_an_oversized_table_into_multiple_table_chunks()
    {
        var chunker = CreateChunker(maxTokens: 20, overlapTokens: 0);
        var manyRows = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Row {i} | Value {i}"));
        var elements = new List<DocumentElement> { new() { Type = ElementType.Table, Text = manyRows } };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.Equal(ChunkType.Table, c.ChunkType));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Lumen.UnitTests --filter ChunkerTests`
Expected: FAIL — compile error, `Chunker`/`ChunkingOptions`/`IChunker` don't exist yet.

- [ ] **Step 4: Write `IChunker` and `ChunkingOptions`**

```csharp
// src/Lumen.Application/Interfaces/IChunker.cs
using Lumen.Application.Ingestion;
using Lumen.Domain;

namespace Lumen.Application.Interfaces;

public interface IChunker
{
    List<DocumentChunk> Chunk(Guid documentId, List<DocumentElement> elements);
}
```

```csharp
// src/Lumen.Infrastructure/DependencyInjection/ChunkingOptions.cs
namespace Lumen.Infrastructure.DependencyInjection;

public class ChunkingOptions
{
    public const string SectionName = "Chunking";
    public int MaxTokensPerChunk { get; set; } = 250;
    public int OverlapTokens { get; set; } = 60;
}
```

- [ ] **Step 5: Write `Chunker`**

```csharp
// src/Lumen.Infrastructure/Chunking/Chunker.cs
using Lumen.Application.Ingestion;
using Lumen.Application.Interfaces;
using Lumen.Domain;
using Lumen.Infrastructure.DependencyInjection;
using Microsoft.ML.Tokenizers;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Text;

namespace Lumen.Infrastructure.Chunking;

public class Chunker : IChunker
{
    private readonly int _maxTokensPerChunk;
    private readonly int _overlapTokens;
    private readonly TiktokenTokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");

    public Chunker(IOptions<ChunkingOptions> options)
    {
        _maxTokensPerChunk = options.Value.MaxTokensPerChunk;
        _overlapTokens = options.Value.OverlapTokens;
    }

    public List<DocumentChunk> Chunk(Guid documentId, List<DocumentElement> elements)
    {
        var chunks = new List<DocumentChunk>();
        var proseRun = new List<string>();
        var position = 0;

        void FlushProseRun()
        {
            if (proseRun.Count == 0)
                return;

            var paragraphs = TextChunker.SplitPlainTextParagraphs(proseRun, _maxTokensPerChunk, _overlapTokens, tokenCounter: CountTokens);
            foreach (var paragraph in paragraphs)
            {
                var tokenCount = CountTokens(paragraph);
                chunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    ChunkIndex = chunks.Count,
                    Text = paragraph,
                    TokenCount = tokenCount,
                    CharStart = position,
                    CharEnd = position + paragraph.Length,
                    ChunkType = ChunkType.Prose
                });
                position += paragraph.Length;
            }
            proseRun.Clear();
        }

        foreach (var element in elements)
        {
            if (element.Type == ElementType.Table)
            {
                FlushProseRun();
                foreach (var tableChunkText in SplitTableIfOversized(element.Text))
                {
                    chunks.Add(new DocumentChunk
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = documentId,
                        ChunkIndex = chunks.Count,
                        Text = tableChunkText,
                        TokenCount = CountTokens(tableChunkText),
                        CharStart = position,
                        CharEnd = position + tableChunkText.Length,
                        ChunkType = ChunkType.Table
                    });
                    position += tableChunkText.Length;
                }
                continue;
            }

            proseRun.Add(element.Text);
        }

        FlushProseRun();
        return chunks;
    }

    private IEnumerable<string> SplitTableIfOversized(string tableText)
    {
        if (CountTokens(tableText) <= _maxTokensPerChunk)
        {
            yield return tableText;
            yield break;
        }

        var rows = tableText.Split('\n');
        var currentGroup = new List<string>();
        var currentTokens = 0;

        foreach (var row in rows)
        {
            var rowTokens = CountTokens(row);
            if (currentGroup.Count > 0 && currentTokens + rowTokens > _maxTokensPerChunk)
            {
                yield return string.Join("\n", currentGroup);
                currentGroup = new List<string>();
                currentTokens = 0;
            }

            currentGroup.Add(row);
            currentTokens += rowTokens;
        }

        if (currentGroup.Count > 0)
            yield return string.Join("\n", currentGroup);
    }

    private int CountTokens(string text) => _tokenizer.CountTokens(text);
}
```

Oversized-table splitting here is row-group splitting without header-row duplication (the design flagged header duplication as a nice-to-have, not required) — a simple, testable first pass; revisit if Stage 3/4 retrieval quality on split tables turns out to suffer without it.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Lumen.UnitTests --filter ChunkerTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Lumen.Application/Interfaces/IChunker.cs src/Lumen.Infrastructure/DependencyInjection/ChunkingOptions.cs src/Lumen.Infrastructure/Chunking tests/Lumen.UnitTests/Chunking
git commit -m "feat: add table-aware Chunker over DocumentElement"
```

---

## Task 9: `UploadDocumentCommand` — orchestration

**Files:**
- Create: `src/Lumen.Application/Documents/Commands/UploadDocumentCommand.cs` (record + handler together, matching the existing `CreateDocumentCommand.cs`/`Documents/Commands/` convention)
- Test: `tests/Lumen.UnitTests/Documents/UploadDocumentCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IDocumentReaderFactory` (Task 7), `IChunker` (Task 8), `IFileStorage` (Task 4), `IUnitOfWork` (Task 3), `ICurrentTenant` (Stage 1).
- Produces: `public record UploadDocumentCommand(string FileName, Stream Content, string Title, DateOnly FilingDate) : IRequest<Document>` in namespace `Lumen.Application.Documents.Commands`. Task 11's controller consumes this.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.UnitTests/Documents/UploadDocumentCommandHandlerTests.cs
using Lumen.Application.Ingestion;

namespace Lumen.UnitTests.Documents;

public class UploadDocumentCommandHandlerTests
{
    [Fact]
    public async Task Handle_reads_chunks_stores_the_file_and_persists_the_document()
    {
        var tenantId = Guid.NewGuid();
        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.Setup(t => t.TenantId).Returns(tenantId);

        var elements = new List<DocumentElement> { new() { Type = ElementType.Paragraph, Text = "Some cleaned text." } };
        var reader = new Mock<IDocumentReader>();
        reader.Setup(r => r.ReadAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(elements);

        var readerFactory = new Mock<IDocumentReaderFactory>();
        readerFactory.Setup(f => f.GetReader("apple-10k.htm")).Returns(reader.Object);

        var chunks = new List<DocumentChunk> { new() { Id = Guid.NewGuid(), ChunkIndex = 0, Text = "Some cleaned text.", TokenCount = 3, ChunkType = ChunkType.Prose } };
        var chunker = new Mock<IChunker>();
        chunker.Setup(c => c.Chunk(It.IsAny<Guid>(), elements)).Returns(chunks);

        var fileStorage = new Mock<IFileStorage>();
        fileStorage.Setup(s => s.SaveAsync(tenantId, It.IsAny<Guid>(), ".htm", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync($"{tenantId}/some-id.htm");

        var documentRepository = new Mock<IRepository<Document>>();
        var chunkRepository = new Mock<IDocumentChunkRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Documents).Returns(documentRepository.Object);
        unitOfWork.SetupGet(u => u.Chunks).Returns(chunkRepository.Object);

        var handler = new UploadDocumentCommandHandler(readerFactory.Object, chunker.Object, fileStorage.Object, unitOfWork.Object, currentTenant.Object);
        using var content = new MemoryStream();

        var command = new UploadDocumentCommand("apple-10k.htm", content, "Apple 10-K FY2025", new DateOnly(2025, 10, 31));

        var result = await handler.Handle(command, CancellationToken.None);

        documentRepository.Verify(r => r.Add(It.Is<Document>(d => d.Title == "Apple 10-K FY2025" && d.RawFilePath == $"{tenantId}/some-id.htm")), Times.Once);
        chunkRepository.Verify(r => r.Add(It.IsAny<DocumentChunk>()), Times.Once);
        unitOfWork.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Apple 10-K FY2025", result.Title);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.UnitTests --filter UploadDocumentCommandHandlerTests`
Expected: FAIL — compile error, `UploadDocumentCommand`/`UploadDocumentCommandHandler` don't exist yet.

- [ ] **Step 3: Write `UploadDocumentCommand` and its handler, in one file**

```csharp
// src/Lumen.Application/Documents/Commands/UploadDocumentCommand.cs
namespace Lumen.Application.Documents.Commands;

public record UploadDocumentCommand(
    string FileName,
    Stream Content,
    [Required, MaxLength(500)] string Title,
    DateOnly FilingDate) : IRequest<Document>;

public class UploadDocumentCommandHandler : IRequestHandler<UploadDocumentCommand, Document>
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IChunker _chunker;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;

    public UploadDocumentCommandHandler(
        IDocumentReaderFactory readerFactory,
        IChunker chunker,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentTenant currentTenant)
    {
        _readerFactory = readerFactory;
        _chunker = chunker;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
    }

    public async Task<Document> Handle(UploadDocumentCommand request, CancellationToken ct)
    {
        var reader = _readerFactory.GetReader(request.FileName);

        var elements = await reader.ReadAsync(request.Content, ct);
        request.Content.Position = 0;

        var documentId = Guid.NewGuid();
        var chunks = _chunker.Chunk(documentId, elements);

        var extension = Path.GetExtension(request.FileName);
        var rawFilePath = await _fileStorage.SaveAsync(_currentTenant.TenantId, documentId, extension, request.Content, ct);

        var document = new Document
        {
            Id = documentId,
            Title = request.Title,
            Content = string.Join("\n\n", elements.Select(e => e.Text)),
            RawFilePath = rawFilePath,
            FilingDate = request.FilingDate
        };

        _unitOfWork.Documents.Add(document);
        foreach (var chunk in chunks)
        {
            chunk.DocumentId = documentId;
            _unitOfWork.Chunks.Add(chunk);
        }

        await _unitOfWork.CompleteAsync(ct);
        return document;
    }
}
```

`document.Id`/`chunk.DocumentId` are assigned explicitly here (not left to EF's key generation), for the same reason `Guid.NewGuid()` is already assigned explicitly in `CreateDocumentCommandHandler`-style code per `.research/specification/csharp-project-conventions.md` — the mapped entity created inside the repository's `Add` is a *different* tracked object than the domain model the handler holds onto, so an ID generated only at `SaveChanges` time would never flow back to `document`/`chunks` here.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Lumen.UnitTests --filter UploadDocumentCommandHandlerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Lumen.Application/Documents/Commands/UploadDocumentCommand.cs tests/Lumen.UnitTests/Documents/UploadDocumentCommandHandlerTests.cs
git commit -m "feat: add UploadDocumentCommand orchestrating read, chunk, store, persist"
```

---

## Task 10: `GetDocumentChunksQuery`

**Files:**
- Create: `src/Lumen.Application/Documents/Queries/GetDocumentChunksQuery.cs` (record + handler together, matching the existing `GetDocumentByIdQuery.cs`/`Documents/Queries/` convention)
- Test: `tests/Lumen.UnitTests/Documents/GetDocumentChunksQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork.Chunks` (Task 3).
- Produces: `public record GetDocumentChunksQuery(Guid DocumentId) : IRequest<List<DocumentChunk>>` in namespace `Lumen.Application.Documents.Queries`. Task 11's controller consumes this.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.UnitTests/Documents/GetDocumentChunksQueryHandlerTests.cs
namespace Lumen.UnitTests.Documents;

public class GetDocumentChunksQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_chunks_for_the_requested_document()
    {
        var documentId = Guid.NewGuid();
        var chunks = new List<DocumentChunk> { new() { Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = 0, Text = "x", TokenCount = 1 } };

        var chunkRepository = new Mock<IDocumentChunkRepository>();
        chunkRepository.Setup(r => r.GetByDocumentIdAsync(documentId, It.IsAny<CancellationToken>())).ReturnsAsync(chunks);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Chunks).Returns(chunkRepository.Object);

        var handler = new GetDocumentChunksQueryHandler(unitOfWork.Object);

        var result = await handler.Handle(new GetDocumentChunksQuery(documentId), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(documentId, result[0].DocumentId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.UnitTests --filter GetDocumentChunksQueryHandlerTests`
Expected: FAIL — compile error, types don't exist yet.

- [ ] **Step 3: Write `GetDocumentChunksQuery` and its handler, in one file**

```csharp
// src/Lumen.Application/Documents/Queries/GetDocumentChunksQuery.cs
namespace Lumen.Application.Documents.Queries;

public record GetDocumentChunksQuery(Guid DocumentId) : IRequest<List<DocumentChunk>>;

public class GetDocumentChunksQueryHandler : IRequestHandler<GetDocumentChunksQuery, List<DocumentChunk>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetDocumentChunksQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<List<DocumentChunk>> Handle(GetDocumentChunksQuery request, CancellationToken ct) =>
        _unitOfWork.Chunks.GetByDocumentIdAsync(request.DocumentId, ct);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Lumen.UnitTests --filter GetDocumentChunksQueryHandlerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Lumen.Application/Documents/Queries/GetDocumentChunksQuery.cs tests/Lumen.UnitTests/Documents/GetDocumentChunksQueryHandlerTests.cs
git commit -m "feat: add GetDocumentChunksQuery"
```

---

## Task 11: Api — upload endpoint, chunks endpoint, remove the old create endpoint

**Files:**
- Modify: `src/Lumen.Api/Controllers/DocumentsController.cs` (remove `Create`, add `Upload`, add `GetChunks`)
- Create: `src/Lumen.Api/DTO/Responses/DocumentChunkDto.cs`
- Modify: `src/Lumen.Api/DTO/Responses/DocumentDto.cs` (add `ChunkCount`)
- Modify: `src/Lumen.Api/Mapping/DomainDtoMappingProfile.cs` (add chunk map, extend the existing `CreateMap<Document, DocumentDto>()`)
- Delete: `src/Lumen.Application/Documents/Commands/CreateDocumentCommand.cs`
- Modify: wherever `AddLumenServices()` (`src/Lumen.Infrastructure/DependencyInjection/`) registers repositories/services — register `IDocumentReaderFactory`, `IChunker`, `IFileStorage`, bind `FileStorageOptions`/`ChunkingOptions`
- Test: `tests/Lumen.IntegrationTests/Documents/DocumentUploadTests.cs`

**Interfaces:**
- Consumes: `UploadDocumentCommand` (Task 9), `GetDocumentChunksQuery` (Task 10).
- Produces: `POST /api/documents/upload` (multipart, `Role.Admin`, 201 Created), `GET /api/documents/{id}/chunks`. Task 12/13's tests consume these HTTP routes.

**Real test-utility shapes this task (and Tasks 12/13) rely on**, confirmed from the actual current `tests/Lumen.IntegrationTests/Utils/`:
- `TestWebApplicationFactory.Create(string connectionString) : WebApplicationFactory<Program>` — static factory method, not a constructor.
- `TestDataSeeder.SeedTenantWithUserAsync(string connectionString, string tenantName, Role role = Role.Member) : Task<(Guid TenantId, string Email, string Password)>`.
- `TestAuth.AuthenticateAsync(HttpClient client, string email, string password) : Task` — logs in and sets `client.DefaultRequestHeaders.Authorization` directly.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Lumen.IntegrationTests/Documents/DocumentUploadTests.cs
namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class DocumentUploadTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Upload test tenant", Role.Admin);

        _client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(_client, email, password);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Upload_then_get_chunks_round_trips_a_small_html_document()
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("<html><body><h1>Title</h1><p>Some prose about the business.</p></body></html>"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "test.htm");
        content.Add(new StringContent("Test Document"), "title");
        content.Add(new StringContent("2025-10-31"), "filingDate");

        var uploadResponse = await _client.PostAsync("/api/documents/upload", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<DocumentDto>();
        Assert.NotNull(uploaded);
        Assert.True(uploaded!.ChunkCount > 0);

        var chunksResponse = await _client.GetAsync($"/api/documents/{uploaded.Id}/chunks");
        Assert.Equal(HttpStatusCode.OK, chunksResponse.StatusCode);
        var chunks = await chunksResponse.Content.ReadFromJsonAsync<List<DocumentChunkDto>>();
        Assert.NotEmpty(chunks!);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Lumen.IntegrationTests --filter DocumentUploadTests`
Expected: FAIL — 404, no `/api/documents/upload` route yet.

- [ ] **Step 3: Write the response DTOs**

```csharp
// src/Lumen.Api/DTO/Responses/DocumentChunkDto.cs
namespace Lumen.Api.DTO.Responses;

public class DocumentChunkDto
{
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = default!;
    public int TokenCount { get; set; }
    public string ChunkType { get; set; } = default!;
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
}
```

Extend the existing `DocumentDto` with:

```csharp
public int ChunkCount { get; set; }
```

- [ ] **Step 4: Add the AutoMapper mapping**

```csharp
// src/Lumen.Api/Mapping/DomainDtoMappingProfile.cs
namespace Lumen.Api.Mapping;

public class DomainDtoMappingProfile : Profile
{
    public DomainDtoMappingProfile()
    {
        CreateMap<Document, DocumentDto>()
            .ForMember(dest => dest.ChunkCount, opt => opt.Ignore()); // set explicitly in the controller — chunk count isn't on the Document domain model

        CreateMap<DocumentChunk, DocumentChunkDto>()
            .ForMember(dest => dest.ChunkType, opt => opt.MapFrom(src => src.ChunkType.ToString()));
    }
}
```

This replaces the existing `CreateMap<Document, DocumentDto>();` line (currently bare, no `.ForMember`) with the version above, and adds the new `DocumentChunk` map alongside it in the same constructor.

- [ ] **Step 5: Update `DocumentsController`**

```csharp
// src/Lumen.Api/Controllers/DocumentsController.cs (relevant additions/removals)

// REMOVE the old Create action and its [HttpPost] route entirely.

[HttpPost("upload")]
[Authorize(Roles = nameof(Role.Admin))]
public async Task<ActionResult<DocumentDto>> Upload(IFormFile file, [FromForm] string title, [FromForm] DateOnly filingDate, CancellationToken ct)
{
    await using var stream = file.OpenReadStream();
    var command = new UploadDocumentCommand(file.FileName, stream, title, filingDate);

    var document = await _mediator.Send(command, ct);
    var chunkCount = (await _mediator.Send(new GetDocumentChunksQuery(document.Id), ct)).Count;

    var dto = _mapper.Map<DocumentDto>(document);
    dto.ChunkCount = chunkCount;
    return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
}

[HttpGet("{id:guid}/chunks")]
public async Task<ActionResult<List<DocumentChunkDto>>> GetChunks(Guid id, CancellationToken ct)
{
    var document = await _mediator.Send(new GetDocumentByIdQuery(id), ct);
    if (document is null)
        return NotFound();

    var chunks = await _mediator.Send(new GetDocumentChunksQuery(id), ct);
    return Ok(_mapper.Map<List<DocumentChunkDto>>(chunks));
}
```

The existence check via `GetDocumentByIdQuery` (unchanged from Stage 1) is what makes a cross-tenant `id` 404 rather than an empty `200` — `GetDocumentChunksQuery` alone has no way to distinguish "document exists but has zero chunks" from "document belongs to a different tenant," since both look like an empty result from `IDocumentChunkRepository.GetByDocumentIdAsync`.

`_mediator`/`_mapper` are the same constructor-injected fields already on this controller from Stage 1 (confirmed: `IMediator _mediator`, `IMapper _mapper`) — this task only adds/removes actions, it doesn't touch the constructor. `GetById` (used by `CreatedAtAction`, mirroring the removed `Create` action's exact pattern) is unchanged.

- [ ] **Step 6: Delete `CreateDocumentCommand`**

```bash
git rm src/Lumen.Application/Documents/Commands/CreateDocumentCommand.cs
```

This single file holds both the `CreateDocumentCommand` record and `CreateDocumentCommandHandler` class (confirmed current layout).

- [ ] **Step 7: Register the new services**

```csharp
// wherever AddLumenServices() (Lumen.Infrastructure/DependencyInjection) currently registers repositories/services, add:
services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
services.Configure<ChunkingOptions>(configuration.GetSection(ChunkingOptions.SectionName));
services.AddScoped<IDocumentReaderFactory, DocumentReaderFactory>();
services.AddScoped<IChunker, Chunker>();
services.AddScoped<IFileStorage, LocalFileStorage>();
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/Lumen.IntegrationTests --filter DocumentUploadTests`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add src/Lumen.Api src/Lumen.Infrastructure/DependencyInjection tests/Lumen.IntegrationTests/Documents/DocumentUploadTests.cs
git commit -m "feat: add upload/chunks endpoints, remove plain-text document creation"
```

---

## Task 12: Fix up Stage 1 tests and seed data broken by the removed create endpoint / new required columns

**Files:**
- Modify: `tests/Lumen.IntegrationTests/TenantIsolation/DocumentTenantIsolationTests.cs` — its third test posts to the removed `/api/documents`.
- Delete: `tests/Lumen.IntegrationTests/Documents/CreateDocumentValidationTests.cs` — entirely about the removed endpoint.
- Create: `tests/Lumen.IntegrationTests/Documents/UploadDocumentValidationTests.cs` — equivalent validation coverage for the new endpoint, so the removal above isn't a net coverage loss.
- Modify: `tests/Lumen.IntegrationTests/Utils/TestDataSeeder.cs` — its private `SeedAsync` creates a bare `DocumentEntity` with no `RawFilePath`/`FilingDate`.
- Modify: `src/Lumen.Infrastructure/Persistence/DbSeeder.cs` — same gap in `SeedTenantAsync`.

**Interfaces:**
- Consumes: `POST /api/documents/upload` (Task 11).
- Produces: nothing new — this task repairs existing coverage and seed data broken by Task 2's new required columns and Task 11's endpoint removal, so Stage 1's guarantees still hold.

- [ ] **Step 1: Fix `TestDataSeeder.SeedAsync`**

```csharp
// tests/Lumen.IntegrationTests/Utils/TestDataSeeder.cs — inside the private SeedAsync method, change:
context.Documents.Add(new DocumentEntity { Id = documentId.Value, TenantId = tenantId, Title = doc.Title, Content = doc.Content });
```

to:

```csharp
context.Documents.Add(new DocumentEntity
{
    Id = documentId.Value,
    TenantId = tenantId,
    Title = doc.Title,
    Content = doc.Content,
    RawFilePath = $"{tenantId}/{documentId.Value}.htm",
    FilingDate = DateOnly.FromDateTime(DateTime.UtcNow)
});
```

- [ ] **Step 2: Fix `DbSeeder.SeedTenantAsync`**

```csharp
// src/Lumen.Infrastructure/Persistence/DbSeeder.cs — change:
context.Documents.Add(new DocumentEntity { Id = Guid.NewGuid(), Title = documentTitle, Content = documentContent });
```

to:

```csharp
var seedDocumentId = Guid.NewGuid();
context.Documents.Add(new DocumentEntity
{
    Id = seedDocumentId,
    Title = documentTitle,
    Content = documentContent,
    RawFilePath = $"{tenantId}/{seedDocumentId}.htm",
    FilingDate = DateOnly.FromDateTime(DateTime.UtcNow)
});
```

- [ ] **Step 3: Run the currently-passing suite to confirm it now fails only where expected**

Run: `dotnet test`
Expected: `DocumentTenantIsolationTests.A_document_created_over_HTTP_by_Tenant_A_is_invisible_to_Tenant_B` FAILs (still posts JSON to the now-404 `/api/documents`); `CreateDocumentValidationTests` FAILs the same way (every test in it posts to the removed route). Everything else should still PASS — Steps 1/2 just fixed the seeding gap that Task 2's migration would otherwise have broken everywhere.

- [ ] **Step 4: Fix `DocumentTenantIsolationTests`'s third test**

```csharp
// tests/Lumen.IntegrationTests/TenantIsolation/DocumentTenantIsolationTests.cs — replace:
var created = await client.PostAsJsonAsync("/api/documents", new { title, content = "Revenue is up." });
Assert.Equal(HttpStatusCode.Created, created.StatusCode);
```

with:

```csharp
using var content = new MultipartFormDataContent();
var fileContent = new ByteArrayContent("<html><body><p>Revenue is up.</p></body></html>"u8.ToArray());
fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
content.Add(fileContent, "file", "doc.htm");
content.Add(new StringContent(title), "title");
content.Add(new StringContent("2025-01-01"), "filingDate");

var created = await client.PostAsync("/api/documents/upload", content);
Assert.Equal(HttpStatusCode.Created, created.StatusCode);
```

The rest of the test (the two `GetFromJsonAsync<List<DocumentDto>>("/api/documents")` assertions afterward) is unchanged — only how the document gets created changes.

- [ ] **Step 5: Delete `CreateDocumentValidationTests.cs`, add its upload equivalent**

```bash
git rm tests/Lumen.IntegrationTests/Documents/CreateDocumentValidationTests.cs
```

```csharp
// tests/Lumen.IntegrationTests/Documents/UploadDocumentValidationTests.cs
namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class UploadDocumentValidationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Upload validation", Role.Admin);

        _client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(_client, email, password);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static MultipartFormDataContent BuildUpload(string title)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("<html><body><p>Body.</p></body></html>"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "doc.htm");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("2025-01-01"), "filingDate");
        return content;
    }

    [Fact]
    public async Task Uploading_with_a_title_over_500_characters_returns_400()
    {
        using var content = BuildUpload(new string('x', 501));

        var response = await _client.PostAsync("/api/documents/upload", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_a_valid_document_returns_201()
    {
        using var content = BuildUpload("A title");

        var response = await _client.PostAsync("/api/documents/upload", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
```

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test`
Expected: PASS — all previously-passing Stage 1 tests still pass (now going through upload where relevant); `UploadDocumentValidationTests` covers what `CreateDocumentValidationTests` used to.

- [ ] **Step 7: Commit**

```bash
git add tests/Lumen.IntegrationTests src/Lumen.Infrastructure/Persistence/DbSeeder.cs
git commit -m "fix: migrate Stage 1 tests off the removed plain-text create endpoint, fix seed data for new required columns"
```

---

## Task 13: Tenant isolation for chunks over HTTP + characterization test on a real filing

**Files:**
- Create: `tests/Lumen.IntegrationTests/Documents/DocumentChunkTenantIsolationTests.cs`
- Create: `tests/Lumen.IntegrationTests/Documents/DocumentIngestionCharacterizationTests.cs`
- Create (binary fixture, copied from a real filing — see design.md's Testing section): `tests/Lumen.IntegrationTests/Fixtures/apple-10k-fy2025.htm`

**Interfaces:**
- Consumes: `POST /api/documents/upload`, `GET /api/documents/{id}/chunks` (Task 11).
- Produces: nothing new — this is the flagship-style closing test coverage for Stage 2, mirroring Stage 1's `DocumentTenantIsolationTests`.

- [ ] **Step 1: Copy a real Apple 10-K into the test fixtures**

Copy the real Apple 10-K primary document (the one already downloaded and inspected by hand during design, e.g. from `samples/edgar/`) to `tests/Lumen.IntegrationTests/Fixtures/apple-10k-fy2025.htm`, and ensure it's copied to the test output directory in the `.csproj` (same `<None Include=... CopyToOutputDirectory>` pattern as Task 6's PDF fixture). This file is committed to git — unlike `samples/edgar/`, which is gitignored.

- [ ] **Step 2: Write the failing HTTP-level tenant isolation test for chunks**

```csharp
// tests/Lumen.IntegrationTests/Documents/DocumentChunkTenantIsolationTests.cs
namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class DocumentChunkTenantIsolationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private (string Email, string Password) _tenantA;
    private (string Email, string Password) _tenantB;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, emailA, passwordA) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Chunk isolation A", Role.Admin);
        var (_, emailB, passwordB) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Chunk isolation B", Role.Admin);
        _tenantA = (emailA, passwordA);
        _tenantB = (emailB, passwordB);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> UploadAsync(HttpClient client)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("<html><body><p>Tenant-specific content.</p></body></html>"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "doc.htm");
        content.Add(new StringContent("Doc"), "title");
        content.Add(new StringContent("2025-01-01"), "filingDate");

        var response = await client.PostAsync("/api/documents/upload", content);
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<DocumentDto>();
        return document!.Id;
    }

    [Fact]
    public async Task Tenant_A_cannot_read_Tenant_Bs_chunks_by_id()
    {
        var clientB = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientB, _tenantB.Email, _tenantB.Password);
        var tenantBDocumentId = await UploadAsync(clientB);

        var clientA = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(clientA, _tenantA.Email, _tenantA.Password);

        var response = await clientA.GetAsync($"/api/documents/{tenantBDocumentId}/chunks");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

This exercises `GetChunks`'s existence check from Task 11 (`GetDocumentByIdQuery` first, `NotFound()` if null) together with `AppDbContext`'s global query filter making a cross-tenant `documentId` invisible in the first place — the same two-layer mechanism `GetById` already relies on for `Document` itself, confirmed working in Stage 1's `DocumentTenantIsolationTests.Tenant_A_asking_for_Tenant_Bs_document_by_id_gets_404_not_the_document`.

- [ ] **Step 3: Write the characterization test**

```csharp
// tests/Lumen.IntegrationTests/Documents/DocumentIngestionCharacterizationTests.cs
namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class DocumentIngestionCharacterizationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Characterization", Role.Admin);

        _client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(_client, email, password);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_real_Apple_10K_ingests_without_error_and_respects_the_token_budget()
    {
        var fileBytes = await File.ReadAllBytesAsync(Path.Combine("Fixtures", "apple-10k-fy2025.htm"));
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(fileContent, "file", "apple-10k-fy2025.htm");
        content.Add(new StringContent("Apple 10-K FY2025"), "title");
        content.Add(new StringContent("2025-10-31"), "filingDate");

        var uploadResponse = await _client.PostAsync("/api/documents/upload", content);
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<DocumentDto>();

        var chunksResponse = await _client.GetAsync($"/api/documents/{uploaded!.Id}/chunks");
        var chunks = await chunksResponse.Content.ReadFromJsonAsync<List<DocumentChunkDto>>();

        Assert.True(chunks!.Count > 0);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 250, $"Chunk {c.ChunkIndex} has {c.TokenCount} tokens, exceeding MaxTokensPerChunk"));
    }
}
```

- [ ] **Step 4: Run both tests to verify they fail or pass appropriately**

Run: `dotnet test tests/Lumen.IntegrationTests --filter "DocumentChunkTenantIsolationTests|DocumentIngestionCharacterizationTests"`
Expected: both compile and run against the real pipeline built in Tasks 1-11. The isolation test should PASS immediately (it exercises the same query-filter mechanism already proven in Task 2/Stage 1). The characterization test is the first time the full pipeline runs against a real, large, messy filing — if it fails, this is exactly where the known `TextChunker` stack-overflow risk (flagged in design.md) or the `250`-token assertion would surface; investigate via `systematic-debugging`, don't just raise the assertion threshold to make it pass.

- [ ] **Step 5: Commit**

```bash
git add tests/Lumen.IntegrationTests/Documents/DocumentChunkTenantIsolationTests.cs tests/Lumen.IntegrationTests/Documents/DocumentIngestionCharacterizationTests.cs tests/Lumen.IntegrationTests/Fixtures/apple-10k-fy2025.htm tests/Lumen.IntegrationTests/Lumen.IntegrationTests.csproj
git commit -m "test: extend tenant isolation to chunks, add real-filing characterization test"
```

---

## Task 14: `GlobalUsings.cs` audit for `Lumen.Application` and `Lumen.Infrastructure`

**Files:**
- Modify: `src/Lumen.Application/GlobalUsings.cs`
- Modify: `src/Lumen.Infrastructure/GlobalUsings.cs`
- Modify: any of this stage's new files that end up with a redundant local `using` once a namespace is promoted.

**Interfaces:**
- Consumes: every file created in Tasks 1-13.
- Produces: nothing new — this is a hygiene pass, not a behavior change. Mirrors Stage 1's own post-completion history (per `.research/stage-1/progress.md`, this needed two separate full audits there too) — doing it as its own task here rather than trying to get every individual task's `using` placement perfect inline.

`Lumen.Application.Ingestion` (`DocumentElement`/`ElementType`) is the clearest known candidate: by the end of Task 8 it's referenced by name in at least `IDocumentReader.cs`, `IChunker.cs` (both `Lumen.Application`), and `HtmlDocumentReader.cs`, `PdfDocumentReader.cs`, `Chunker.cs` (`Lumen.Infrastructure`) — well past the 2-file promotion threshold in both projects independently. There may be others depending on exactly how each task's code ended up using local vs. already-global namespaces.

- [ ] **Step 1: Audit `Lumen.Application`**

```bash
# Temporarily empty (don't delete) the file, then build and read the errors:
cp src/Lumen.Application/GlobalUsings.cs /tmp/Application.GlobalUsings.bak
echo "" > src/Lumen.Application/GlobalUsings.cs
dotnet build src/Lumen.Application 2>&1 | grep CS0246
```

Each `CS0246` names a file and a missing type/namespace. Reconstruct: for each namespace that shows up as missing in **2 or more files**, add it back to `GlobalUsings.cs` (in the correct block — `Lumen.*` / `Microsoft.*` / `System.*` / third-party, per `.research/specification/csharp-project-conventions.md` §4.2); for a namespace missing in exactly **1** file, add a local `using` to that file instead. Restore anything from the backup that's still genuinely needed. Discard the backup once the rebuilt file is confirmed correct (`dotnet build src/Lumen.Application` clean).

- [ ] **Step 2: Remove now-redundant local `using` lines in `Lumen.Application`**

For every namespace promoted in Step 1, grep for a local `using` of that same namespace still sitting in an individual file and delete it:

```bash
grep -rn "^using Lumen.Application.Ingestion;" src/Lumen.Application
```

- [ ] **Step 3: Repeat Steps 1-2 for `Lumen.Infrastructure`**

```bash
cp src/Lumen.Infrastructure/GlobalUsings.cs /tmp/Infrastructure.GlobalUsings.bak
echo "" > src/Lumen.Infrastructure/GlobalUsings.cs
dotnet build src/Lumen.Infrastructure 2>&1 | grep CS0246
```

Same reconstruction method, same 2+-file promotion rule, same per-block ordering.

- [ ] **Step 4: Full solution build and test run**

Run: `dotnet build` then `dotnet test`
Expected: clean build, all tests still passing — this task changes only `using` placement, never behavior.

- [ ] **Step 5: Commit**

```bash
git add src/Lumen.Application/GlobalUsings.cs src/Lumen.Infrastructure/GlobalUsings.cs
git commit -m "chore: promote cross-file usings to GlobalUsings.cs for Stage 2 code"
```
