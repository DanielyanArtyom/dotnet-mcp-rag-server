# Lumen Stage 4b (measured retrieval and ingest improvements) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship six independently switchable improvements — an ingest profile stamped on every document and checked by the eval harness, hybrid search (Postgres FTS + RRF), contextual chunk headers, merging of tiny chunks, decimal-safe chunking, and a per-document diversity cap — all defaulting to off, so the user can walk a measured ladder by flipping config.

**Architecture:** Search stays one hand-written SQL path per mode inside `ChunkSearchRepository` (tenant predicate written by hand in every CTE); a `Search` options section picks the mode and the cap. Ingest changes live in `Chunker` behind `Chunking` switches; `IngestProfileProvider` turns the effective switches into a string stored on each `Document`, and the eval harness refuses to run when any document's profile differs from the server's. No new packages; one EF migration per schema change.

**Tech Stack:** .NET 10, ASP.NET Core, MediatR 14, AutoMapper 16, EF Core 10 + Npgsql (built-in `tsvector`/`NpgsqlTsVector`), pgvector, `Microsoft.SemanticKernel.Text.TextChunker`, `Microsoft.ML.Tokenizers`, xUnit, Moq, Testcontainers.PostgreSql.

**Spec:** `.research/stage-4b/design.md` (inputs: `.research/discussion/stage-4b-design-decisions.md`, `.research/stage-4/hybrid-search-postgres-fts-rrf.md`, `.research/discussion/stage-4-incident-mixed-chunking.md`).

## Global Constraints

- **Never run `git commit` or `git push`.** `CLAUDE.md` forbids it. Where a step says "snapshot", the controller records a tree; implementers never stage or commit.
- Never print or edit `src/Lumen.Api/appsettings.Development.json`'s secrets; Azure settings live in user-secrets. Never call Azure or the user's running server. All tests run on `Fake` providers.
- Integration tests: `DOCKER_API_VERSION=1.43 dotnet test tests/Lumen.IntegrationTests`.
- **Every new switch defaults to off**: `Search:Mode = Vector`, `Search:MaxChunksPerDocument = null`, `Chunking:ContextHeaders = false`, `Chunking:MergeBelowTokens = 0`, `Chunking:ProtectDecimals = false`. With all switches off, `/api/search` and `/api/ask` must return the same chunks in the same order as before this plan.
- **Tenant predicate in every CTE** of every hand-written search statement (`c."TenantId" = @tenantId`) plus the existing `Documents` join with `d."TenantId" = @tenantId`. No new `.IgnoreQueryFilters()` call site (`grep -rn "IgnoreQueryFilters" src/` → exactly one hit, `UserLookupService.cs`).
- `ChunkSearchResult.Distance` always means cosine distance (computed for every returned row, in every mode). `Score` is the RRF score, `null` in vector mode.
- The chunk budget (`Chunking:MaxTokensPerChunk`, 250) is never exceeded by any step; `TokenCount` counts `Text` only (never the header).
- `Text` of a chunk never contains the context header; the prompt (`PromptConstants`, `RagPrompt`) is not changed by this plan — `promptHash` stays `ae0bb62cae85`.
- Ingest profile format, exact: `chunk={Max}/{Overlap};hdr={v1|off};merge={N};dec={v1|off}`, e.g. `chunk=250/60;hdr=off;merge=0;dec=off`.
- DI-constructed classes use a traditional constructor + `private readonly` fields; Options classes live in `Lumen.Infrastructure/DependencyInjection/`; DTOs only in `Lumen.Api/DTO/{Requests,Responses}`; `using` rules per `CLAUDE.md` (2+ files → `GlobalUsings.cs`, block order `Lumen.*` → `Microsoft.*` → `System.*` → third-party; no unused usings) — **checked at the end of every task**. Comments only for non-obvious *why*.
- Migrations: `dotnet ef migrations add <Name> -p src/Lumen.Infrastructure -s src/Lumen.Api`. Both the app (`ApplicationBuilderExtension`) and `PostgresFixture` apply migrations on start.

## Pre-implementation corrections (checked against the real code, 2026-10-06)

These **override** the spec's text where they differ. Each was raised to the user with the plan.

1. **Section per chunk needs a flush at headings.** Today `Chunker` packs prose lines across element boundaries (`TextChunker.SplitPlainTextParagraphs` over one long run), so a prose chunk has no single section. When `Chunking:ContextHeaders` is on, the prose run is **flushed at every section heading**, so a prose chunk never spans two sections and its section is the heading that opened it. With the switch off, chunking is byte-for-byte unchanged. Consequence: turning headers on also changes chunk boundaries (more, slightly smaller prose chunks); the run header's per-document chunk counts make that visible.
2. **The chunker receives title and filing date.** The spec kept the chunker free of document metadata; in code the cleanest seam is a small record `ChunkingContext(string DocumentTitle, DateOnly FilingDate)` passed to `IChunker.Chunk(...)`, and a static `ContextHeaderBuilder` that both the chunker and tests use. The upload handler passes the context.
3. **Diversity cap is soft.** A hard "≤ M per document" would cut a single-document tenant's context from 5 to 3 sources. Instead, results are ordered by `(doc_rank > @maxPerDocument)` first, then by rank: chunks within the cap come first, over-cap chunks only backfill when no other document has candidates. With two relevant documents, at most M of topK come from one; with one document, topK are still returned.
4. **Merging keeps the receiver's type and header.** The spec said "the larger neighbour's"; the receiving chunk is the larger one in practice, and keeping its own `ChunkType`/`ContextHeader` is simpler and deterministic.
5. **The vector SQL is rewritten into a candidates CTE** (`ORDER BY distance, Id LIMIT @candidates`, then the cap, then `LIMIT @topK`) so the cap can be applied in both modes. With the cap off and `@candidates ≥ @topK` the result is identical to today's (tie-break by `Id` added). `@candidates = Math.Max(Search:Candidates, topK)`.
6. **`ChunkingOptions` gets validation.** It is bound today with `Configure<>()` and no validation; it moves to `AddOptions<>().Bind().ValidateOnStart()` with a `ChunkingOptionsValidator` (budget > 0, `0 ≤ Overlap < Max`, `0 ≤ MergeBelowTokens < Max`).
7. **`IChunkSearchRepository.SearchAsync` gains `string queryText`** (second parameter). Callers: `SearchChunksQueryHandler`, `AskQuestionQueryHandler`, `ChunkSearchRepositoryFilingDateTests`, and the Moq setups in `SearchChunksQueryHandlerTests` / `AskQuestionQueryHandlerTests`.
8. **`GET /api/settings`**: `searchMode` keeps its name and becomes `Search:Mode` lower-cased (`"vector"`/`"hybrid"`); a `search` object and a top-level `ingestProfile` are added. Existing assertions (`searchMode == "vector"`) stay valid.
9. **The harness stub tests** (`tests/Lumen.Eval.Tests/Running/EvalRunnerTests.cs`) return settings and documents without `ingestProfile`; Task 2 updates the stub to return matching profiles and adds a mismatch case.
10. **The ingest profile is also part of the eval run header's corpus**: `RunHeader.Corpus` lists tenant, title, profile and chunk count per document (chunk counts come from the chunk lists the harness already downloads for anchor lint).

## Review Focus

1. **A question with no lexemes** ("What is it?", "???", only stop words) in hybrid mode must return the vector results, not a SQL error or an empty list. Pinned in Task 3 (`Hybrid_search_with_only_stop_words_falls_back_to_vector_ranks`).
2. **A question with tsquery operators and quotes** ("Apple's R&D (FY'25) | !cost: 'x'") must not raise a tsquery syntax error. Pinned in Task 3 (`Hybrid_search_tolerates_tsquery_operators_in_the_question`).
3. **A rare word present in two tenants' documents** must never surface the other tenant's chunk through the lexical branch, even with `VectorWeight = 0`. Pinned in Task 3 (`Hybrid_search_never_returns_another_tenants_lexical_match`).
4. **A tenant with a single document and `MaxChunksPerDocument = 3`** must still get topK sources. Pinned in Task 4 (`Cap_backfills_when_only_one_document_has_candidates`).
5. **A tiny chunk whose both neighbours are already at the budget, or a tiny chunk at the very start of a document,** must be kept / merged forward without ever exceeding the budget. Pinned in Task 6 (`Keeps_a_short_chunk_when_neither_neighbour_has_room`, `Merges_a_leading_short_chunk_into_the_next_one`).

---

## Task 1: Ingest profile on the document

**Files:**
- Modify: `src/Lumen.Infrastructure/DependencyInjection/ChunkingOptions.cs`
- Create: `src/Lumen.Infrastructure/DependencyInjection/ChunkingOptionsValidator.cs`
- Create: `src/Lumen.Application/Interfaces/IIngestProfileProvider.cs`
- Create: `src/Lumen.Infrastructure/Chunking/IngestProfileProvider.cs`
- Modify: `src/Lumen.Domain/Entities/Document.cs`, `src/Lumen.Infrastructure/Persistence/Entities/DocumentEntity.cs`, `src/Lumen.Infrastructure/Persistence/Configurations/DocumentEntityConfiguration.cs`
- Create: migration `AddDocumentIngestProfile` (generated)
- Modify: `src/Lumen.Application/Documents/Commands/UploadDocumentCommand.cs`
- Modify: `src/Lumen.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`, `src/Lumen.Api/Extensions/WebApplicationBuilderExtensions.cs`
- Modify: `src/Lumen.Api/DTO/Responses/DocumentDto.cs`, `src/Lumen.Api/DTO/Responses/SettingsDto.cs`, `src/Lumen.Api/Controllers/SettingsController.cs`
- Test: `tests/Lumen.UnitTests/Chunking/IngestProfileProviderTests.cs`, `tests/Lumen.UnitTests/Chunking/ChunkingOptionsValidatorTests.cs`, `tests/Lumen.UnitTests/Documents/UploadDocumentCommandHandlerTests.cs` (existing), `tests/Lumen.IntegrationTests/Documents/DocumentIngestProfileTests.cs`

**Interfaces:**
- Produces: `ChunkingOptions { int MaxTokensPerChunk = 250; int OverlapTokens = 60; bool ContextHeaders; int MergeBelowTokens; bool ProtectDecimals }`; `IIngestProfileProvider { string Current { get; } }` (singleton); `Document.IngestProfile : string?`; `DocumentDto.IngestProfile : string?`; `SettingsDto.IngestProfile : string`.

- [ ] **Step 1: Failing tests for the profile string and the validator**

`tests/Lumen.UnitTests/Chunking/IngestProfileProviderTests.cs`:

```csharp
namespace Lumen.UnitTests.Chunking;

public class IngestProfileProviderTests
{
    private static string Profile(ChunkingOptions options) => new IngestProfileProvider(Options.Create(options)).Current;

    [Fact]
    public void All_switches_off_by_default() =>
        Assert.Equal("chunk=250/60;hdr=off;merge=0;dec=off", Profile(new ChunkingOptions()));

    [Fact]
    public void Every_switch_is_reflected() =>
        Assert.Equal("chunk=400/100;hdr=v1;merge=20;dec=v1", Profile(new ChunkingOptions
        {
            MaxTokensPerChunk = 400, OverlapTokens = 100, ContextHeaders = true, MergeBelowTokens = 20, ProtectDecimals = true
        }));
}
```

`tests/Lumen.UnitTests/Chunking/ChunkingOptionsValidatorTests.cs`:

```csharp
namespace Lumen.UnitTests.Chunking;

public class ChunkingOptionsValidatorTests
{
    private readonly ChunkingOptionsValidator _validator = new();

    [Fact]
    public void Defaults_are_valid() => Assert.True(_validator.Validate(null, new ChunkingOptions()).Succeeded);

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(250, 250, 0)]
    [InlineData(250, -1, 0)]
    [InlineData(250, 60, 250)]
    [InlineData(250, 60, -1)]
    public void Rejects_inconsistent_values(int max, int overlap, int merge) =>
        Assert.True(_validator.Validate(null, new ChunkingOptions { MaxTokensPerChunk = max, OverlapTokens = overlap, MergeBelowTokens = merge }).Failed);
}
```

Run: `dotnet test tests/Lumen.UnitTests --filter "FullyQualifiedName~IngestProfile|FullyQualifiedName~ChunkingOptionsValidator"` → compile errors (RED).

- [ ] **Step 2: Options, validator, provider**

`ChunkingOptions.cs` — add:

```csharp
    public bool ContextHeaders { get; set; }
    public int MergeBelowTokens { get; set; }
    public bool ProtectDecimals { get; set; }
```

`ChunkingOptionsValidator.cs`:

```csharp
namespace Lumen.Infrastructure.DependencyInjection;

public class ChunkingOptionsValidator : IValidateOptions<ChunkingOptions>
{
    public ValidateOptionsResult Validate(string? name, ChunkingOptions options)
    {
        var failures = new List<string>();

        if (options.MaxTokensPerChunk < 1)
        {
            failures.Add("Chunking:MaxTokensPerChunk must be positive.");
        }

        if (options.OverlapTokens < 0 || options.OverlapTokens >= options.MaxTokensPerChunk)
        {
            failures.Add("Chunking:OverlapTokens must be at least 0 and smaller than MaxTokensPerChunk.");
        }

        if (options.MergeBelowTokens < 0 || options.MergeBelowTokens >= options.MaxTokensPerChunk)
        {
            failures.Add("Chunking:MergeBelowTokens must be at least 0 (0 = off) and smaller than MaxTokensPerChunk.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
```

`src/Lumen.Application/Interfaces/IIngestProfileProvider.cs`:

```csharp
namespace Lumen.Application.Interfaces;

public interface IIngestProfileProvider
{
    string Current { get; }
}
```

`src/Lumen.Infrastructure/Chunking/IngestProfileProvider.cs`:

```csharp
namespace Lumen.Infrastructure.Chunking;

public class IngestProfileProvider : IIngestProfileProvider
{
    public IngestProfileProvider(IOptions<ChunkingOptions> options)
    {
        var o = options.Value;
        Current = $"chunk={o.MaxTokensPerChunk}/{o.OverlapTokens};hdr={(o.ContextHeaders ? "v1" : "off")};merge={o.MergeBelowTokens};dec={(o.ProtectDecimals ? "v1" : "off")}";
    }

    public string Current { get; }
}
```

`WebApplicationBuilderExtensions.Configure()` — replace `builder.Services.Configure<ChunkingOptions>(...)` with:

```csharp
        builder.Services.AddOptions<ChunkingOptions>()
            .Bind(builder.Configuration.GetSection(ChunkingOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ChunkingOptions>, ChunkingOptionsValidator>();
```

`ServiceCollectionExtensions.AddLumenServices()` — add `services.AddSingleton<IIngestProfileProvider, IngestProfileProvider>();`.

Run the Step 1 filter → PASS.

- [ ] **Step 3: Stamp the profile on upload**

`Document` and `DocumentEntity` — add `public string? IngestProfile { get; set; }`. `DocumentEntityConfiguration` — add `builder.Property(d => d.IngestProfile).HasMaxLength(200);`.

`UploadDocumentCommandHandler` — inject `IIngestProfileProvider ingestProfile` (traditional constructor, new `private readonly IIngestProfileProvider _ingestProfile;`) and set `IngestProfile = _ingestProfile.Current` in `BuildDocument` (make `BuildDocument` an instance method or pass the string in). Update `UploadDocumentCommandHandlerTests`: construct the handler with a `Mock<IIngestProfileProvider>` returning `"chunk=250/60;hdr=off;merge=0;dec=off"` and add:

```csharp
    [Fact]
    public async Task Stamps_the_current_ingest_profile_on_the_document()
    {
        // arrange as the existing happy-path test does, then:
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("chunk=250/60;hdr=off;merge=0;dec=off", result.Document.IngestProfile);
    }
```

(Use the existing test file's arrange helpers verbatim — read it first.)

Generate the migration: `dotnet ef migrations add AddDocumentIngestProfile -p src/Lumen.Infrastructure -s src/Lumen.Api`. Expected `Up`: one nullable `character varying(200)` column `IngestProfile` on `Documents`; no backfill.

- [ ] **Step 4: Expose the profile**

`DocumentDto` — add (always written, `null` for pre-profile documents):

```csharp
    public string? IngestProfile { get; set; }
```

`SettingsDto` — add `public string IngestProfile { get; set; } = default!;`. `SettingsController` — inject `IIngestProfileProvider` and set `IngestProfile = _ingestProfile.Current`.

- [ ] **Step 5: Integration test**

`tests/Lumen.IntegrationTests/Documents/DocumentIngestProfileTests.cs`:

```csharp
using System.Text.Json;

namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class DocumentIngestProfileTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Uploaded_documents_and_settings_report_the_same_profile()
    {
        using var factory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Profile tenant", Role.Admin);
        using var client = factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, email, password);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("<html><body><p>Net sales rose.</p></body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent("Profiled"), "title");
        content.Add(new StringContent("2025-09-27"), "filingDate");
        (await client.PostAsync("/api/documents/upload", content)).EnsureSuccessStatusCode();

        var documents = JsonDocument.Parse(await client.GetStringAsync("/api/documents")).RootElement;
        var settings = JsonDocument.Parse(await client.GetStringAsync("/api/settings")).RootElement;

        Assert.Equal("chunk=250/60;hdr=off;merge=0;dec=off", settings.GetProperty("ingestProfile").GetString());
        Assert.All(documents.EnumerateArray(), d => Assert.Equal(settings.GetProperty("ingestProfile").GetString(), d.GetProperty("ingestProfile").GetString()));
    }
}
```

Run: `DOCKER_API_VERSION=1.43 dotnet test tests/Lumen.IntegrationTests --filter DocumentIngestProfileTests` → PASS. Full suites + `dotnet build` (0 warnings) + `MappingProfileTests` green. Using pass. Snapshot.

---

## Task 2: Harness — corpus check, corpus in the header, sources in results

**Files:**
- Modify: `tools/Lumen.Eval/Api/ApiContracts.cs`, `tools/Lumen.Eval/Running/RunModels.cs`, `tools/Lumen.Eval/Running/EvalRunner.cs`, `tools/Lumen.Eval/Running/ConsoleReport.cs`
- Test: `tests/Lumen.Eval.Tests/Running/EvalRunnerTests.cs` (existing stub), `tests/Lumen.Eval.Tests/Running/RunStoreTests.cs`, `tests/Lumen.Eval.Tests/Running/ConsoleReportTests.cs`, `tests/Lumen.IntegrationTests/Eval/EvalRunnerTests.cs`

**Interfaces:**
- Consumes: `ingestProfile` on `GET /api/documents` items and on `GET /api/settings` (Task 1); `score` on search hits / ask sources (added by Task 3 — nullable here, so this task does not depend on it).
- Produces: `DocumentInfo(Guid Id, string Title, string? IngestProfile = null)`; `SearchHit(..., double Distance, double? Score = null)`; `AskSource(..., string Text, double? Score = null)`; `record CorpusDocument(string Tenant, string Title, string? IngestProfile, int ChunkCount)`; `record SourceRecord(int Rank, Guid ChunkId, Guid DocumentId, int ChunkIndex, string ChunkType, double Distance, double? Score, string TextPreview)`; `RunHeader.IngestProfile : string?`, `RunHeader.Corpus : List<CorpusDocument>`; `QuestionResult.Sources : List<SourceRecord>`.

- [ ] **Step 1: Update the stub and write the failing tests**

In `tests/Lumen.Eval.Tests/Running/EvalRunnerTests.cs`, give `StubHandler` a `string documentProfile = "p1"` parameter; return settings `{"chat":{"deployment":"…"},"ingestProfile":"p1"}` and documents `[{"id":"…","title":"Own","ingestProfile":"<documentProfile>"}]`. Thread `documentProfile` through `Runner(...)`. Add:

```csharp
    [Fact]
    public async Task Refuses_to_run_when_a_document_was_ingested_under_another_profile()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(Runner(null, withJudge: false, foreignHits: false, documentProfile: "old"), RunMode.Retrieval));

        Assert.Contains("Own", ex.Message);
        Assert.Contains("old", ex.Message);
        Assert.Contains("p1", ex.Message);
    }

    [Fact]
    public async Task Records_the_corpus_and_the_retrieved_sources()
    {
        var outcome = await Run(Runner(null, withJudge: false, foreignHits: false), RunMode.Retrieval);

        Assert.Equal("p1", outcome.Header.IngestProfile);
        var document = Assert.Single(outcome.Header.Corpus);
        Assert.Equal(("acme", "Own", "p1", 1), (document.Tenant, document.Title, document.IngestProfile, document.ChunkCount));
        var source = Assert.Single(outcome.Results[0].Sources);
        Assert.Equal(1, source.Rank);
        Assert.StartsWith("net sales", source.TextPreview);
    }
```

Extend `RunStoreTests.Writes_and_reloads_a_run` so the result carries one `SourceRecord` and the header one `CorpusDocument`, and assert both survive the round trip. Extend `ConsoleReportTests` to assert the header prints `ingest profile: p1` and a corpus line containing `Own` and `1 chunks`.

Run: `dotnet test tests/Lumen.Eval.Tests` → RED.

- [ ] **Step 2: Contracts and models**

`ApiContracts.cs`:

```csharp
public record DocumentInfo(Guid Id, string Title, string? IngestProfile = null);

public record SearchHit(Guid ChunkId, Guid DocumentId, string DocumentTitle, int ChunkIndex, string ChunkType, int TokenCount, string Text, double Distance, double? Score = null);

public record AskSource(int SourceId, Guid ChunkId, Guid DocumentId, string DocumentTitle, int ChunkIndex, string ChunkType, int TokenCount, double Distance, bool Cited, string Text, double? Score = null);
```

`RunModels.cs` — add:

```csharp
public record CorpusDocument(string Tenant, string Title, string? IngestProfile, int ChunkCount);

public record SourceRecord(int Rank, Guid ChunkId, Guid DocumentId, int ChunkIndex, string ChunkType, double Distance, double? Score, string TextPreview);
```

`RunHeader` — add `public string? IngestProfile { get; set; }` and `public List<CorpusDocument> Corpus { get; set; } = [];`. `QuestionResult` — add `public List<SourceRecord> Sources { get; set; } = [];`.

- [ ] **Step 3: Runner**

In `EvalRunner.RunAsync`, after `ServerSettings` is read:

```csharp
        header.IngestProfile = header.ServerSettings.TryGetProperty("ingestProfile", out var profile) ? profile.GetString() : null;
```

In the document loop, record each document: `header.Corpus.Add(new CorpusDocument(tenant, document.Title, document.IngestProfile, chunks.Count));` (store the downloaded chunk list in a local `chunks` first). After the loop, before anchor lint:

```csharp
        var stale = header.Corpus.Where(d => header.IngestProfile is null || d.IngestProfile != header.IngestProfile).ToList();
        if (stale.Count > 0)
        {
            throw new InvalidOperationException(
                $"The server ingests with profile '{header.IngestProfile ?? "(none reported)"}', but these documents were ingested differently — re-upload them before running the eval: "
                + string.Join("; ", stale.Select(d => $"{d.Tenant}/{d.Title} = '{d.IngestProfile ?? "(none)"}'")));
        }
```

This sits before the question loop, so it surfaces as `InvalidOperationException` → exit 1 (not wrapped in `EvalRunAbortedException`).

Sources — add a helper and call it from both modes:

```csharp
    private static List<SourceRecord> ToSources<T>(IReadOnlyList<T> items, Func<T, (Guid ChunkId, Guid DocumentId, int ChunkIndex, string ChunkType, double Distance, double? Score, string Text)> project) =>
        items.Select((item, index) =>
        {
            var s = project(item);
            return new SourceRecord(index + 1, s.ChunkId, s.DocumentId, s.ChunkIndex, s.ChunkType, s.Distance, s.Score,
                s.Text.Length <= 200 ? s.Text : s.Text[..200]);
        }).ToList();
```

`RunRetrievalAsync`: `result.Sources = ToSources(hits, h => (h.ChunkId, h.DocumentId, h.ChunkIndex, h.ChunkType, h.Distance, h.Score, h.Text));`. `RunFullAsync`: same over `ask.Sources`.

- [ ] **Step 4: Console header**

`ConsoleReport.PrintRun`, after the server-settings block:

```csharp
        output.WriteLine($"ingest profile: {header.IngestProfile ?? "(none)"}");
        foreach (var d in header.Corpus)
        {
            output.WriteLine($"  {d.Tenant} / {d.Title}: {d.ChunkCount} chunks");
        }
```

- [ ] **Step 5: Green + end-to-end**

`dotnet test tests/Lumen.Eval.Tests` → PASS. The integration `tests/Lumen.IntegrationTests/Eval/EvalRunnerTests.cs` must still pass (documents uploaded through the real host carry the current profile). Add there:

```csharp
    [Fact]
    public async Task A_run_against_a_server_with_a_different_profile_is_refused()
    {
        using var otherProfile = _factory.WithWebHostBuilder(b => b.UseSetting("Chunking:ProtectDecimals", "true"));
        var runner = new EvalRunner(new LumenApiClient(otherProfile.CreateClient()), _tenants, judge: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(Set(), "inline", new string('0', 64), new RunOptions { Mode = RunMode.Retrieval, Label = "t" }, CancellationToken.None));
    }
```

Run harness + integration filters → PASS; whole-solution build 0 warnings; using pass. Snapshot.

---

## Task 3: Hybrid search (FTS + RRF)

**Files:**
- Create: `src/Lumen.Infrastructure/DependencyInjection/SearchMode.cs`, `SearchOptions.cs`, `SearchOptionsValidator.cs`
- Modify: `src/Lumen.Domain/Entities/DocumentChunk.cs` (+`ContextHeader`), `src/Lumen.Infrastructure/Persistence/Entities/DocumentChunkEntity.cs` (+`ContextHeader`), `src/Lumen.Infrastructure/Persistence/Configurations/DocumentChunkEntityConfiguration.cs`
- Create: migration `AddHybridSearch` (generated)
- Modify: `src/Lumen.Application/Interfaces/IChunkSearchRepository.cs`, `src/Lumen.Infrastructure/Repositories/ChunkSearchRepository.cs`, `ChunkSearchRow.cs`, `src/Lumen.Domain/Models/ChunkSearchResult.cs`, `src/Lumen.Domain/Models/AnswerSource.cs`
- Modify: `src/Lumen.Application/Search/SearchChunksQuery.cs`, `src/Lumen.Application/Ask/AskQuestionQuery.cs`
- Modify: `src/Lumen.Api/DTO/Responses/SearchResultDto.cs`, `AskResponseDto.cs` (`AskSourceDto`), `SettingsDto.cs`, `src/Lumen.Api/Controllers/SettingsController.cs`, `src/Lumen.Api/Extensions/WebApplicationBuilderExtensions.cs`, `src/Lumen.Api/appsettings.json`
- Test: `tests/Lumen.UnitTests/Search/SearchOptionsValidatorTests.cs`; existing Moq setups in `SearchChunksQueryHandlerTests`, `AskQuestionQueryHandlerTests`; `tests/Lumen.IntegrationTests/Search/HybridSearchTests.cs`; `tests/Lumen.IntegrationTests/TenantIsolation/HybridSearchTenantIsolationTests.cs`; existing `ChunkSearchRepositoryFilingDateTests`, `SettingsEndpointTests`

**Interfaces:**
- Produces: `enum SearchMode { Vector, Hybrid }`; `SearchOptions` (section `"Search"`): `SearchMode Mode = Vector; int RrfK = 60; int Candidates = 50; double LexicalWeight = 1.0; double VectorWeight = 1.0; int? MaxChunksPerDocument = null`; `IChunkSearchRepository.SearchAsync(float[] queryEmbedding, string queryText, string embeddingModel, int topK, Guid? documentId, CancellationToken ct = default)`; `ChunkSearchResult.Score : double?`; `AnswerSource.Score : double?`; `SearchResultDto.Score`, `AskSourceDto.Score : double?`; `DocumentChunk.ContextHeader : string?` (populated by Task 5); `SettingsDto.Search : SearchSettingsDto { Mode, RrfK, Candidates, LexicalWeight, VectorWeight, MaxChunksPerDocument }`.

- [ ] **Step 1: Options + validator (TDD)**

`tests/Lumen.UnitTests/Search/SearchOptionsValidatorTests.cs`:

```csharp
namespace Lumen.UnitTests.Search;

public class SearchOptionsValidatorTests
{
    private readonly SearchOptionsValidator _validator = new();

    [Fact]
    public void Defaults_are_valid() => Assert.True(_validator.Validate(null, new SearchOptions()).Succeeded);

    [Theory]
    [InlineData(0, 50, 1.0, 1.0, null)]
    [InlineData(60, 0, 1.0, 1.0, null)]
    [InlineData(60, 201, 1.0, 1.0, null)]
    [InlineData(60, 50, -0.1, 1.0, null)]
    [InlineData(60, 50, 0.0, 0.0, null)]
    [InlineData(60, 50, 1.0, 1.0, 0)]
    public void Rejects_invalid_values(int rrfK, int candidates, double lexical, double vector, int? maxPerDocument) =>
        Assert.True(_validator.Validate(null, new SearchOptions
        {
            RrfK = rrfK, Candidates = candidates, LexicalWeight = lexical, VectorWeight = vector, MaxChunksPerDocument = maxPerDocument
        }).Failed);
}
```

Implementation:

```csharp
namespace Lumen.Infrastructure.DependencyInjection;

public enum SearchMode
{
    Vector,
    Hybrid
}
```

```csharp
namespace Lumen.Infrastructure.DependencyInjection;

public class SearchOptions
{
    public const string SectionName = "Search";

    public SearchMode Mode { get; set; } = SearchMode.Vector;
    public int RrfK { get; set; } = 60;
    public int Candidates { get; set; } = 50;
    public double LexicalWeight { get; set; } = 1.0;
    public double VectorWeight { get; set; } = 1.0;
    public int? MaxChunksPerDocument { get; set; }
}
```

```csharp
namespace Lumen.Infrastructure.DependencyInjection;

public class SearchOptionsValidator : IValidateOptions<SearchOptions>
{
    public ValidateOptionsResult Validate(string? name, SearchOptions options)
    {
        var failures = new List<string>();

        if (options.RrfK < 1)
        {
            failures.Add("Search:RrfK must be positive.");
        }

        if (options.Candidates is < 1 or > 200)
        {
            failures.Add("Search:Candidates must be between 1 and 200.");
        }

        if (options.LexicalWeight < 0 || options.VectorWeight < 0 || options.LexicalWeight + options.VectorWeight <= 0)
        {
            failures.Add("Search:LexicalWeight and Search:VectorWeight must be non-negative and not both zero.");
        }

        if (options.MaxChunksPerDocument is < 1)
        {
            failures.Add("Search:MaxChunksPerDocument must be at least 1 when set.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
```

Bind in `WebApplicationBuilderExtensions.Configure()` like `Chat` (`AddOptions<SearchOptions>().Bind(...).ValidateOnStart()` + `AddSingleton<IValidateOptions<SearchOptions>, SearchOptionsValidator>()`). `appsettings.json` — add after `Chat`:

```json
  "Search": {
    "Mode": "Vector",
    "RrfK": 60,
    "Candidates": 50,
    "LexicalWeight": 1.0,
    "VectorWeight": 1.0,
    "MaxChunksPerDocument": null
  },
```

- [ ] **Step 2: Schema — `ContextHeader` + generated `SearchVector`**

`DocumentChunk` and `DocumentChunkEntity` — add `public string? ContextHeader { get; set; }` (the AutoMapper profile maps it by name in both directions). `DocumentChunkEntityConfiguration.Configure` — add:

```csharp
        // Shadow property: the tsvector is computed by Postgres from the stored text and never read by the app,
        // so neither the domain model nor the mapping profile needs to know it exists.
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql("""to_tsvector('english', coalesce("ContextHeader", '') || ' ' || "Text")""", stored: true);
        builder.HasIndex("SearchVector").HasMethod("GIN");
```

(`NpgsqlTsVector` is in `NpgsqlTypes`; add the using per the rules.) Generate: `dotnet ef migrations add AddHybridSearch -p src/Lumen.Infrastructure -s src/Lumen.Api`. Expected `Up`: nullable `text` `ContextHeader`; `tsvector` `SearchVector` with `computedColumnSql` and `stored: true`; GIN index `IX_DocumentChunks_SearchVector`. Existing rows get their vector computed by Postgres when the column is added.

- [ ] **Step 3: Failing integration tests**

`tests/Lumen.IntegrationTests/Search/HybridSearchTests.cs`:

```csharp
namespace Lumen.IntegrationTests.Search;

[Collection("Postgres collection")]
public class HybridSearchTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _baseFactory = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _baseFactory = TestWebApplicationFactory.Create(fixture.ConnectionString);
        _factory = _baseFactory.WithWebHostBuilder(b => b.UseSetting("Search:Mode", "Hybrid"));
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, "Hybrid tenant", Role.Admin);
        _client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(_client, email, password);

        await UploadAsync("Filler one", "<p>Operating expenses grew with headcount and infrastructure costs.</p>");
        await UploadAsync("Filler two", "<p>Services revenue rose on advertising and cloud services.</p>");
        await UploadAsync("Rare word", "<p>The zebracorn index closed the year higher.</p>");
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        _baseFactory.Dispose();
        return Task.CompletedTask;
    }

    private async Task UploadAsync(string title, string body)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"<html><body>{body}</body></html>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "doc.htm");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent("2025-09-27"), "filingDate");
        (await _client.PostAsync("/api/documents/upload", content)).EnsureSuccessStatusCode();
    }

    private async Task<List<SearchResultDto>> SearchAsync(string query) =>
        (await (await _client.PostAsJsonAsync("/api/search", new { query, topK = 3 })).Content.ReadFromJsonAsync<List<SearchResultDto>>())!;

    [Fact]
    public async Task Hybrid_search_ranks_a_lexical_only_match_first()
    {
        var results = await SearchAsync("Which index is called zebracorn?");

        Assert.Contains("zebracorn", results[0].Text);
        Assert.NotNull(results[0].Score);
    }

    [Fact]
    public async Task Hybrid_search_with_only_stop_words_falls_back_to_vector_ranks()
    {
        var results = await SearchAsync("What is it?");

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Hybrid_search_tolerates_tsquery_operators_in_the_question()
    {
        var response = await _client.PostAsJsonAsync("/api/search", new { query = "Apple's R&D (FY'25) | !cost: 'x' <-> zebracorn", topK = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Settings_report_the_hybrid_mode()
    {
        var settings = JsonDocument.Parse(await _client.GetStringAsync("/api/settings")).RootElement;

        Assert.Equal("hybrid", settings.GetProperty("searchMode").GetString());
        Assert.Equal(60, settings.GetProperty("search").GetProperty("rrfK").GetInt32());
    }
}
```

(Fake embeddings carry no meaning, so only the lexical branch can put the zebracorn chunk first — that makes the first test sharp. `System.Text.Json`/`JsonDocument` and `SearchResultDto` usings per the rules.)

`tests/Lumen.IntegrationTests/TenantIsolation/HybridSearchTenantIsolationTests.cs`: seed tenants A and B, upload `"<p>The zebracorn index closed the year higher.</p>"` into **both** (titles "A filing" / "B filing"), then with a factory using `Search:Mode=Hybrid` **and** `Search:VectorWeight=0` query as B `"zebracorn"` with `topK = 10` and assert:

```csharp
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(documentB, r.DocumentId));
        Assert.DoesNotContain(results, r => r.DocumentId == documentA);
```

Name the test `Hybrid_search_never_returns_another_tenants_lexical_match`. Add a second test running the same with default weights (`Hybrid_search_never_returns_another_tenants_chunk_for_identical_text`).

Run the two filters → RED (no hybrid mode / no `score`).

- [ ] **Step 4: Repository**

`IChunkSearchRepository.SearchAsync` — add `string queryText` as the second parameter. Update `SearchChunksQueryHandler` (`request.Query`), `AskQuestionQueryHandler.RetrieveAsync` (`request.Question`), `ChunkSearchRepositoryFilingDateTests` (pass `"Net sales rose."`) and the Moq setups/verifies (add `It.IsAny<string>()` in second position; in `AskQuestionQueryHandlerTests.Retrieves_with_the_given_top_k_and_document_then_generates` verify `"What were net sales?"` is passed).

`ChunkSearchRow` — add `public double? Score { get; set; }`. `ChunkSearchResult` and `AnswerSource` — add `public double? Score { get; set; }`; map it in the repository projection and in `AskQuestionQueryHandler.ToSources` (`Score = chunk.Score`). `SearchResultDto` and `AskSourceDto` — add `public double? Score { get; set; }` (AutoMapper maps by name).

`ChunkSearchRepository` — inject `IOptions<SearchOptions>`; keep the existing class comment and add `HybridSql`:

```csharp
    // Both branches carry their own tenant predicate: the lexical CTE is a second hand-written path the
    // global query filter cannot reach, and identical text in two tenants is exactly where it would leak.
    // The tsquery is built as an OR of the question's own lexemes: plainto/websearch_to_tsquery AND every
    // word and match nothing on real questions.
    private const string HybridSql = """
        WITH q AS (
            SELECT array_to_string(array(
                       SELECT quote_literal(l) FROM unnest(tsvector_to_array(to_tsvector('english', @queryText))) l),
                   ' | ') AS terms
        ),
        lexical AS (
            SELECT c."Id",
                   row_number() OVER (ORDER BY ts_rank_cd(c."SearchVector", q.terms::tsquery) DESC, c."Id") AS rnk
            FROM "DocumentChunks" c, q
            WHERE q.terms <> ''
              AND c."TenantId" = @tenantId
              AND c."EmbeddingModel" = @model
              AND (@documentId IS NULL OR c."DocumentId" = @documentId)
              AND c."SearchVector" @@ q.terms::tsquery
            ORDER BY rnk
            LIMIT @candidates
        ),
        semantic AS (
            SELECT c."Id",
                   row_number() OVER (ORDER BY c."Embedding" <=> @query, c."Id") AS rnk
            FROM "DocumentChunks" c
            WHERE c."TenantId" = @tenantId
              AND c."EmbeddingModel" = @model
              AND (@documentId IS NULL OR c."DocumentId" = @documentId)
            ORDER BY rnk
            LIMIT @candidates
        ),
        fused AS (
            SELECT COALESCE(l."Id", s."Id") AS "Id",
                   COALESCE(@lexicalWeight / (@rrfK + l.rnk), 0) + COALESCE(@vectorWeight / (@rrfK + s.rnk), 0) AS "Score"
            FROM lexical l
            FULL OUTER JOIN semantic s ON s."Id" = l."Id"
        )
        SELECT c."Id" AS "ChunkId",
               c."DocumentId",
               d."Title" AS "DocumentTitle",
               d."FilingDate" AS "DocumentFilingDate",
               c."ChunkIndex",
               c."ChunkType",
               c."TokenCount",
               c."Text",
               (c."Embedding" <=> @query)::double precision AS "Distance",
               f."Score"::double precision AS "Score"
        FROM fused f
        JOIN "DocumentChunks" c ON c."Id" = f."Id" AND c."TenantId" = @tenantId
        JOIN "Documents" d ON d."Id" = c."DocumentId" AND d."TenantId" = @tenantId
        ORDER BY f."Score" DESC, c."Id"
        LIMIT @topK
        """;
```

Add `NULL::double precision AS "Score"` to the existing vector `Sql` select list. In `SearchAsync` choose `_options.Mode == SearchMode.Hybrid ? HybridSql : Sql`; parameters add `queryText` (string), `candidates` (`Math.Max(_options.Candidates, topK)`), `rrfK` (`(double)_options.RrfK`), `lexicalWeight`, `vectorWeight` (doubles). Add the hybrid-only parameters (`queryText`, `rrfK`, `lexicalWeight`, `vectorWeight`) only when the hybrid statement is used, so every parameter sent appears in the SQL.

- [ ] **Step 5: Settings**

`SettingsDto` — add `public SearchSettingsDto Search { get; set; } = new();` and the nested class (`string Mode`, `int RrfK`, `int Candidates`, `double LexicalWeight`, `double VectorWeight`, `int? MaxChunksPerDocument`). `SettingsController` — inject `IOptions<SearchOptions>`, drop the `SearchMode` constant, set `SearchMode = _search.Mode.ToString().ToLowerInvariant()` and fill `Search`.

- [ ] **Step 6: Green**

Run the hybrid and isolation filters → PASS; then all three suites, `dotnet build` 0 warnings, `MappingProfileTests`, `grep -rn "IgnoreQueryFilters" src/` → 1. Using pass. Snapshot.

---

## Task 4: Per-document diversity cap (soft)

**Files:**
- Modify: `src/Lumen.Infrastructure/Repositories/ChunkSearchRepository.cs`
- Test: `tests/Lumen.IntegrationTests/Search/DiversityCapTests.cs`

**Interfaces:**
- Consumes: `SearchOptions.MaxChunksPerDocument`, `@candidates` (Task 3).

- [ ] **Step 1: Failing tests**

`tests/Lumen.IntegrationTests/Search/DiversityCapTests.cs` — one fresh tenant with exactly two documents built from **tables** (each `<table>` is an atomic chunk, so chunk counts are exact): **Big** = 6 tables `<table><tr><td>Total net sales {i}</td><td>{i}00</td></tr></table>`, **Small** = 2 such tables. Query `"Total net sales"`, `topK = 5`. The assertions do not depend on how Fake embeddings rank the chunks: with the soft cap, the 3 best Big chunks and both Small chunks are "within cap" and fill the 5 slots whatever their order.

```csharp
    [Theory]
    [InlineData("Vector")]
    [InlineData("Hybrid")]
    public async Task At_most_three_of_five_come_from_one_document_when_another_has_candidates(string mode)
    {
        var results = await SearchAsync(mode, maxPerDocument: 3);

        Assert.Equal(5, results.Count);
        Assert.True(results.Count(r => r.DocumentId == _big) <= 3);
        Assert.Equal(2, results.Count(r => r.DocumentId == _small));
    }

    [Fact]
    public async Task Without_a_cap_results_are_unchanged() =>
        Assert.Equal(5, (await SearchAsync("Vector", maxPerDocument: null)).Count);

    [Fact]
    public async Task Cap_backfills_when_only_one_document_has_candidates()
    {
        var results = await SearchAsync("Vector", maxPerDocument: 3, documentId: _big);

        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.Equal(_big, r.DocumentId));
    }
```

`SearchAsync(mode, maxPerDocument, documentId = null)` builds a factory with `UseSetting("Search:Mode", mode)` and, when not null, `UseSetting("Search:MaxChunksPerDocument", value)`, then posts `/api/search` with `{ query, topK = 5, documentId }`.

Run → RED (cap ignored).

- [ ] **Step 2: Implement the cap in both statements**

Vector `Sql` becomes:

```sql
WITH ranked AS (
    SELECT c."Id", c."DocumentId", (c."Embedding" <=> @query)::double precision AS dist
    FROM "DocumentChunks" c
    WHERE c."TenantId" = @tenantId
      AND c."EmbeddingModel" = @model
      AND (@documentId IS NULL OR c."DocumentId" = @documentId)
    ORDER BY dist, c."Id"
    LIMIT @candidates
),
capped AS (
    SELECT r.*, row_number() OVER (PARTITION BY r."DocumentId" ORDER BY r.dist, r."Id") AS doc_rank
    FROM ranked r
)
SELECT c."Id" AS "ChunkId", c."DocumentId", d."Title" AS "DocumentTitle", d."FilingDate" AS "DocumentFilingDate",
       c."ChunkIndex", c."ChunkType", c."TokenCount", c."Text",
       r.dist AS "Distance", NULL::double precision AS "Score"
FROM capped r
JOIN "DocumentChunks" c ON c."Id" = r."Id" AND c."TenantId" = @tenantId
JOIN "Documents" d ON d."Id" = c."DocumentId" AND d."TenantId" = @tenantId
ORDER BY (@maxPerDocument IS NOT NULL AND r.doc_rank > @maxPerDocument), r.dist, c."Id"
LIMIT @topK
```

Hybrid — replace the `fused` CTE and the final select of `HybridSql` with:

```sql
fused AS (
    SELECT COALESCE(l."Id", s."Id") AS "Id",
           COALESCE(@lexicalWeight / (@rrfK + l.rnk), 0) + COALESCE(@vectorWeight / (@rrfK + s.rnk), 0) AS "Score"
    FROM lexical l
    FULL OUTER JOIN semantic s ON s."Id" = l."Id"
),
capped AS (
    SELECT f."Id", f."Score",
           row_number() OVER (PARTITION BY c."DocumentId" ORDER BY f."Score" DESC, f."Id") AS doc_rank
    FROM fused f
    JOIN "DocumentChunks" c ON c."Id" = f."Id" AND c."TenantId" = @tenantId
)
SELECT c."Id" AS "ChunkId", c."DocumentId", d."Title" AS "DocumentTitle", d."FilingDate" AS "DocumentFilingDate",
       c."ChunkIndex", c."ChunkType", c."TokenCount", c."Text",
       (c."Embedding" <=> @query)::double precision AS "Distance",
       f."Score"::double precision AS "Score"
FROM capped f
JOIN "DocumentChunks" c ON c."Id" = f."Id" AND c."TenantId" = @tenantId
JOIN "Documents" d ON d."Id" = c."DocumentId" AND d."TenantId" = @tenantId
ORDER BY (@maxPerDocument IS NOT NULL AND f.doc_rank > @maxPerDocument), f."Score" DESC, c."Id"
LIMIT @topK
```

(the `q`, `lexical` and `semantic` CTEs are unchanged). Keep the class comment explaining why the cap is soft (a single-document tenant must still get topK). Parameter `maxPerDocument` as `NpgsqlDbType.Integer` with `DBNull` when null; `candidates` is now passed in both modes.

- [ ] **Step 3: Green**

Diversity filter → PASS; `VectorSearchTests`, `VectorSearchTenantIsolationTests`, `SearchTenantIsolationTests`, hybrid tests unchanged and green; full suites; build 0 warnings. Snapshot.

---

## Task 5: Contextual chunk headers

**Files:**
- Create: `src/Lumen.Application/Ingestion/ChunkingContext.cs`, `src/Lumen.Application/Ingestion/EmbeddingInput.cs`
- Create: `src/Lumen.Infrastructure/Chunking/ContextHeaderBuilder.cs`, `src/Lumen.Infrastructure/Chunking/SectionHeading.cs`
- Modify: `src/Lumen.Application/Interfaces/IChunker.cs`, `src/Lumen.Infrastructure/Chunking/Chunker.cs`, `src/Lumen.Application/Documents/Commands/UploadDocumentCommand.cs`, `src/Lumen.Api/DTO/Responses/DocumentChunkDto.cs`
- Test: `tests/Lumen.UnitTests/Chunking/SectionHeadingTests.cs`, `ContextHeaderBuilderTests.cs`, `ChunkerTests.cs` (existing + new), `UploadDocumentCommandHandlerTests.cs` (existing)

**Interfaces:**
- Produces: `record ChunkingContext(string DocumentTitle, DateOnly FilingDate)`; `IChunker.Chunk(Guid documentId, List<DocumentElement> elements, ChunkingContext context)`; `static class ContextHeaderBuilder { string Build(ChunkingContext context, string? section) }`; `static class SectionHeading { bool IsHeadingLike(string text) }`; `static class EmbeddingInput { string For(DocumentChunk chunk) }`; `DocumentChunkDto.ContextHeader : string?`.

- [ ] **Step 1: Failing unit tests**

`SectionHeadingTests.cs`:

```csharp
namespace Lumen.UnitTests.Chunking;

public class SectionHeadingTests
{
    [Theory]
    [InlineData("Gross Margin")]
    [InlineData("Capital Return Program")]
    [InlineData("Item 7. Management’s Discussion and Analysis")]
    [InlineData("Mac")]
    [InlineData("Services (1)")]
    public void Recognises_heading_like_paragraphs(string text) => Assert.True(SectionHeading.IsHeadingLike(text));

    [Theory]
    [InlineData("Net sales increased during 2025.")]
    [InlineData("First Quarter 2025:")]
    [InlineData("•MacBook Pro")]
    [InlineData("(1) Services net sales include amortization")]
    [InlineData("Americas | 45781")]
    [InlineData("2025")]
    [InlineData("This sentence has far too many words to be any kind of section heading")]
    public void Rejects_sentences_bullets_tables_and_bare_numbers(string text) => Assert.False(SectionHeading.IsHeadingLike(text));
}
```

`ContextHeaderBuilderTests.cs`:

```csharp
namespace Lumen.UnitTests.Chunking;

public class ContextHeaderBuilderTests
{
    private static readonly ChunkingContext Context = new("Apple 10-k", new DateOnly(2025, 9, 27));

    [Fact]
    public void Includes_title_filing_date_and_section() =>
        Assert.Equal("Apple 10-k (filed 2025-09-27) — Operating Expenses", ContextHeaderBuilder.Build(Context, "Operating Expenses"));

    [Fact]
    public void Omits_the_section_when_none_is_known() =>
        Assert.Equal("Apple 10-k (filed 2025-09-27)", ContextHeaderBuilder.Build(Context, null));
}
```

New `ChunkerTests` (change the helper to `CreateChunker(int maxTokens = 250, int overlapTokens = 60, bool contextHeaders = false, int mergeBelowTokens = 0, bool protectDecimals = false)` passing all five into `ChunkingOptions`; every existing call `chunker.Chunk(id, elements)` becomes `chunker.Chunk(id, elements, Context)` with a shared `private static readonly ChunkingContext Context = new("Doc", new DateOnly(2025, 9, 27));`):

```csharp
    [Fact]
    public void Headers_are_off_by_default()
    {
        var chunks = CreateChunker().Chunk(Guid.NewGuid(), [new() { Type = ElementType.Paragraph, Text = "Net sales rose." }], Context);

        Assert.Null(Assert.Single(chunks).ContextHeader);
    }

    [Fact]
    public void Each_chunk_gets_the_section_that_precedes_it()
    {
        var chunker = CreateChunker(contextHeaders: true);
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Paragraph, Text = "Gross Margin" },
            new() { Type = ElementType.Paragraph, Text = "Gross margin increased on a favorable mix." },
            new() { Type = ElementType.Paragraph, Text = "Operating Expenses" },
            new() { Type = ElementType.Paragraph, Text = "Operating expenses for 2025 were as follows:" },
            new() { Type = ElementType.Table, Text = "Research and development | $ | 34,550" }
        };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

        Assert.Equal("Doc (filed 2025-09-27) — Gross Margin", chunks[0].ContextHeader);
        Assert.Contains("favorable mix", chunks[0].Text);
        Assert.DoesNotContain("Operating", chunks[0].Text);
        Assert.Equal("Doc (filed 2025-09-27) — Operating Expenses", chunks.Single(c => c.ChunkType == ChunkType.Table).ContextHeader);
        Assert.All(chunks, c => Assert.DoesNotContain("filed", c.Text));
    }

    [Fact]
    public void Header_tokens_do_not_count_against_the_budget()
    {
        var chunker = CreateChunker(maxTokens: 50, overlapTokens: 0, contextHeaders: true);

        var chunks = chunker.Chunk(Guid.NewGuid(), [new() { Type = ElementType.Paragraph, Text = Sentences(400) }], Context);

        Assert.All(chunks, c => Assert.True(c.TokenCount <= 50));
    }
```

`EmbeddingInput` test (in `tests/Lumen.UnitTests/Domain/DocumentChunkTests.cs` or a new `Ingestion/EmbeddingInputTests.cs`):

```csharp
    [Fact]
    public void Prefixes_the_header_when_present()
    {
        Assert.Equal("H\nT", EmbeddingInput.For(new DocumentChunk { ContextHeader = "H", Text = "T" }));
        Assert.Equal("T", EmbeddingInput.For(new DocumentChunk { Text = "T" }));
    }
```

Run → RED.

- [ ] **Step 2: Implement**

```csharp
namespace Lumen.Application.Ingestion;

public record ChunkingContext(string DocumentTitle, DateOnly FilingDate);
```

```csharp
namespace Lumen.Application.Ingestion;

public static class EmbeddingInput
{
    public static string For(DocumentChunk chunk) =>
        chunk.ContextHeader is null ? chunk.Text : chunk.ContextHeader + "\n" + chunk.Text;
}
```

```csharp
using System.Globalization;

namespace Lumen.Infrastructure.Chunking;

public static class ContextHeaderBuilder
{
    public static string Build(ChunkingContext context, string? section)
    {
        var header = $"{context.DocumentTitle} (filed {context.FilingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})";
        return section is null ? header : $"{header} — {section}";
    }
}
```

```csharp
namespace Lumen.Infrastructure.Chunking;

public static class SectionHeading
{
    private const int MaxWords = 8;
    private const int MaxLength = 80;

    // SEC filings almost never mark headings with <h1>–<h6>; "Gross Margin" or "Capital Return Program"
    // arrive as short standalone paragraphs, so a heading is recognised by shape, not by tag.
    public static bool IsHeadingLike(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length is 0 or > MaxLength || trimmed.Contains('|'))
        {
            return false;
        }

        if (!char.IsLetterOrDigit(trimmed[0]) || !trimmed.Any(char.IsLetter) || trimmed[^1] is '.' or ':' or ';' or ',')
        {
            return false;
        }

        return trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxWords;
    }
}
```

`IChunker.Chunk(Guid documentId, List<DocumentElement> elements, ChunkingContext context)`.

`Chunker`: read `ContextHeaders` from options. Keep a `string? section`. In the element loop, before handling a non-table element: if `_contextHeaders && (element.Type == ElementType.Heading || SectionHeading.IsHeadingLike(element.Text))` → `FlushProseRun(); section = element.Text.Trim();` (the heading text still goes into the prose lines as today). Pass the current section into `AddChunk`, which sets `ContextHeader = _contextHeaders ? ContextHeaderBuilder.Build(context, currentSection) : null`. With `_contextHeaders == false` nothing else changes (no extra flushes).

`UploadDocumentCommandHandler`: `_chunker.Chunk(documentId, elements, new ChunkingContext(request.Title, request.FilingDate))`; `EmbedAsync` sends `chunks.Select(EmbeddingInput.For)`. Update `UploadDocumentCommandHandlerTests` Moq setups to the 3-argument `Chunk` and add a test that a chunk with `ContextHeader = "H"` is embedded as `"H\n<text>"`.

`DocumentChunkDto` — add `public string? ContextHeader { get; set; }`.

- [ ] **Step 3: Green + characterization**

Unit filters → PASS. The integration `DocumentIngestionCharacterizationTests` (Apple 10-K, 296 chunks) must stay green with defaults (headers off). Add one integration assertion there with `Chunking:ContextHeaders=true`: every chunk has a non-null `contextHeader` starting with the document title, and at least one header contains `— Gross Margin`. Full suites, build 0 warnings, using pass. Snapshot.

---

## Task 6: Merge short chunks

**Files:**
- Modify: `src/Lumen.Infrastructure/Chunking/Chunker.cs`
- Test: `tests/Lumen.UnitTests/Chunking/ChunkerTests.cs`

**Interfaces:**
- Consumes: `ChunkingOptions.MergeBelowTokens` (Task 1).

- [ ] **Step 1: Failing tests**

```csharp
    [Fact]
    public void Merges_a_short_chunk_into_the_previous_one()
    {
        var chunker = CreateChunker(mergeBelowTokens: 20);
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Table, Text = "Research and development | $ | 34,550 | $ | 31,370" },
            new() { Type = ElementType.Table, Text = "Apple Inc. | 2025 Form 10-K | 24" }
        };

        var chunk = Assert.Single(chunker.Chunk(Guid.NewGuid(), elements, Context));

        Assert.Contains("34,550", chunk.Text);
        Assert.EndsWith("Apple Inc. | 2025 Form 10-K | 24", chunk.Text);
        Assert.Equal(0, chunk.ChunkIndex);
    }

    [Fact]
    public void Merges_a_leading_short_chunk_into_the_next_one()
    {
        var chunker = CreateChunker(mergeBelowTokens: 20);
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Table, Text = "Page 1" },
            new() { Type = ElementType.Table, Text = "Research and development | $ | 34,550 | $ | 31,370" }
        };

        var chunk = Assert.Single(chunker.Chunk(Guid.NewGuid(), elements, Context));

        Assert.StartsWith("Page 1", chunk.Text);
    }

    [Fact]
    public void Keeps_a_short_chunk_when_neither_neighbour_has_room()
    {
        var chunker = CreateChunker(maxTokens: 30, overlapTokens: 0, mergeBelowTokens: 10);
        var full = string.Join(" | ", Enumerable.Range(1, 14).Select(i => $"{i}0"));
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Table, Text = full },
            new() { Type = ElementType.Table, Text = "Page 2" },
            new() { Type = ElementType.Table, Text = full }
        };

        var chunks = chunker.Chunk(Guid.NewGuid(), elements, Context);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 30));
        Assert.Equal(new[] { 0, 1, 2 }, chunks.Select(c => c.ChunkIndex));
    }

    [Fact]
    public void Merging_is_off_by_default()
    {
        var elements = new List<DocumentElement>
        {
            new() { Type = ElementType.Table, Text = "Research and development | $ | 34,550" },
            new() { Type = ElementType.Table, Text = "Page 3" }
        };

        Assert.Equal(2, CreateChunker().Chunk(Guid.NewGuid(), elements, Context).Count);
    }
```

(Assert in the third test that `full` really is near the budget: `Assert.True(Tokenizer-count(full) > 20)` via a chunk's `TokenCount` — or pick `full` so its token count is 25–30; adjust the range in the test if the tokenizer disagrees, keeping the property "previous + short and short + next both exceed 30".)

Run → RED.

- [ ] **Step 2: Implement**

At the end of `Chunk(...)`, when `_mergeBelowTokens > 0`, run `MergeShortChunks(chunks)`:

```csharp
    // A short chunk is a page footer, a page number or a one-row table fragment; on its own it competes for
    // top-K slots with near-zero information. It is absorbed by a neighbour only if the budget still holds.
    private List<DocumentChunk> MergeShortChunks(List<DocumentChunk> chunks)
    {
        var result = new List<DocumentChunk>();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (chunk.TokenCount > _mergeBelowTokens)
            {
                result.Add(chunk);
                continue;
            }

            if (result.Count > 0 && TryAppend(result[^1], chunk))
            {
                continue;
            }

            if (i + 1 < chunks.Count && TryPrepend(chunks[i + 1], chunk))
            {
                continue;
            }

            result.Add(chunk);
        }

        for (var i = 0; i < result.Count; i++)
        {
            result[i].ChunkIndex = i;
        }

        return result;
    }

    private bool TryAppend(DocumentChunk receiver, DocumentChunk shortChunk)
    {
        var text = receiver.Text + "\n" + shortChunk.Text;
        var tokens = CountTokens(text);
        if (tokens > _maxTokensPerChunk)
        {
            return false;
        }

        receiver.Text = text;
        receiver.TokenCount = tokens;
        receiver.CharEnd = shortChunk.CharEnd;
        return true;
    }

    private bool TryPrepend(DocumentChunk receiver, DocumentChunk shortChunk)
    {
        var text = shortChunk.Text + "\n" + receiver.Text;
        var tokens = CountTokens(text);
        if (tokens > _maxTokensPerChunk)
        {
            return false;
        }

        receiver.Text = text;
        receiver.TokenCount = tokens;
        receiver.CharStart = shortChunk.CharStart;
        return true;
    }
```

- [ ] **Step 3: Green**

Unit filter → PASS; `DocumentIngestionCharacterizationTests` unchanged with defaults; add an assertion with `Chunking:MergeBelowTokens=20` that no chunk of the Apple 10-K fixture has `tokenCount ≤ 20` unless it could not be merged (assert the count drops below the default run's count and all `tokenCount ≤ 250`). Full suites, build, using pass. Snapshot.

---

## Task 7: Keep decimals intact

**Files:**
- Modify: `src/Lumen.Infrastructure/Chunking/Chunker.cs`
- Test: `tests/Lumen.UnitTests/Chunking/ChunkerTests.cs`

**Interfaces:**
- Consumes: `ChunkingOptions.ProtectDecimals` (Task 1).

- [ ] **Step 1: Failing tests**

```csharp
    private static string LongLineWith(string number) =>
        string.Join(" ", Enumerable.Range(0, 30).Select(i => $"Item {i} rose by {number} billion compared with the prior period"));

    [Theory]
    [InlineData("$10.7")]
    [InlineData("$19.8")]
    [InlineData("4.00%")]
    [InlineData("$200.00 per share")]
    public void Protected_decimals_survive_line_splitting(string number)
    {
        var chunker = CreateChunker(maxTokens: 120, overlapTokens: 0, protectDecimals: true);

        var chunks = chunker.Chunk(Guid.NewGuid(), [new() { Type = ElementType.Paragraph, Text = LongLineWith(number) }], Context);

        var all = string.Join("\n", chunks.Select(c => c.Text));
        Assert.DoesNotMatch(@"\d\.\s+\d", all);
        Assert.DoesNotContain("․", all);
        Assert.Contains(number, all);
    }

    [Fact]
    public void Without_protection_the_splitter_breaks_decimals()
    {
        var chunks = CreateChunker(maxTokens: 120, overlapTokens: 0).Chunk(Guid.NewGuid(), [new() { Type = ElementType.Paragraph, Text = LongLineWith("$10.7") }], Context);

        Assert.Matches(@"\d\.\s+\d", string.Join("\n", chunks.Select(c => c.Text)));
    }
```

(The second test documents the defect this step fixes. If `TextChunker` does not split this synthetic line on the decimal point, adjust `LongLineWith` until it does — the point of the test is to reproduce the real "$10. 7" / "$19.\n8" breakage seen in the corpus; it must fail-to-reproduce loudly rather than silently pass.)

Run → RED.

- [ ] **Step 2: Implement**

```csharp
    // TextChunker splits long lines on '.', including the one inside "10.7", and rejoins the halves with a
    // space or newline ("$10. 7 billion"). Swapping the decimal point for a non-delimiter character before
    // splitting, and back afterwards, keeps numbers whole; tokens are always counted on the restored text.
    private const char DecimalPlaceholder = '․';
    private static readonly Regex DecimalPoint = new(@"(?<=\d)\.(?=\d)", RegexOptions.Compiled);

    private string Protect(string text) => _protectDecimals ? DecimalPoint.Replace(text, DecimalPlaceholder.ToString()) : text;

    private static string Restore(string text) => text.Replace(DecimalPlaceholder, '.');
```

Apply `Protect` to each element's text before it enters `SplitPlainTextLines` / table splitting; `CountTokens` becomes `Tokenizer.CountTokens(Restore(text))`; `AddChunk` stores `Restore(text)`.

- [ ] **Step 3: Green**

Unit filter → PASS; all suites; build; using pass. Snapshot.

---

## Task 8: Documentation and the ladder checklist

**Files:**
- Modify: `CLAUDE.md`
- Modify: `.research/README.md`
- Create: `.research/checklists/stage-4b-user-checklist.md`

- [ ] **Step 1: `CLAUDE.md`**

- "Project status": add a **Stage 4b** paragraph — implementation complete, switches off by default, ladder pending (checklist path); the `Search` section; the ingest profile and the harness refusing to run on a profile mismatch; test counts from the final run.
- "Commands": "**Re-upload rule:** any change to a `Chunking` setting changes the ingest profile; delete and re-upload all filings, or the eval harness refuses to run."
- Correct the Stage 2 sentence about decimals if present (root cause: chunker).

- [ ] **Step 2: Ladder checklist (Russian)**

`.research/checklists/stage-4b-user-checklist.md` — `- [ ]` items, each with the exact user-secrets / config change, whether to re-upload, the two runs with labels, and the `compare` command:

0. Re-upload all four filings (stamps profiles) → `run --mode retrieval --label b0-retrieval`, `run --mode full --label b0-full` (new baseline on eval-set `8a02fdeeef19`).
1. `Search:Mode=Hybrid` → `b1-*` → `compare b0-full b1-full` (and retrieval).
2. `Chunking:ContextHeaders=true` → re-upload → `b2-*` → compare with `b1`.
3. `Chunking:MergeBelowTokens=20` → re-upload → `b3-*` → compare with `b2`.
4. `Chunking:ProtectDecimals=true` → re-upload → `b4-*` → compare with `b3`.
5. `Search:MaxChunksPerDocument=3` → `b5-*` → compare with `b4`.
6. For any step that made things worse: turn it back off and say why in `stage-4-hands-on-findings.md`; the next `compare` is against the last kept rung.
7. After the ladder: E1-style breakdown of the remaining failures (now readable from `results.jsonl` `sources`) → decide DECISIONS.md entries.

- [ ] **Step 3: README index + final verification**

Add `stage-4b/plan.md` and the checklist to `.research/README.md`. Run `dotnet build` (0 warnings), `dotnet test tests/Lumen.UnitTests`, `dotnet test tests/Lumen.Eval.Tests`, `DOCKER_API_VERSION=1.43 dotnet test tests/Lumen.IntegrationTests` → all green; `grep -rn "IgnoreQueryFilters" src/` → 1; record counts in `CLAUDE.md`. Snapshot. **No commit.**
