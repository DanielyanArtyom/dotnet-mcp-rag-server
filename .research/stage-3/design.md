# Lumen — Stage 3 design spec: Embeddings + pgvector storage + exact vector search

Status: approved by the user 2026-09-20, feeds `writing-plans` → `plan.md`. Drafted via brainstorming on top of three research files in this folder (`embeddings-dotnet-and-azure-openai.md`, `pgvector-and-dotnet.md`, `dev-and-test-without-azure.md`); the decision log with the alternatives that were rejected is `.research/discussion/stage-3-design-decisions.md`.

## Scope

Stage 3 turns Stage 2's chunks into searchable vectors: an embedding step inside the existing upload pipeline, a `vector` column on `DocumentChunks`, and a `POST /api/search` endpoint that runs an exact (no ANN index) nearest-neighbour query in hand-written SQL with the tenant predicate written by hand.

Explicitly **out of scope**: answer generation / context assembly / sampling parameters (Stage 4), the eval set (Stage 4), hybrid search (Stage 4), HNSW or any ANN index (Stage 4 at the earliest — Stage 3 is exact search by the project plan), an MCP server (Stage 5), a re-index/re-embed endpoint over the stored raw file (deliberately dropped — parameter experiments re-upload the file instead), background jobs or queues (the project has none and upload stays synchronous), embedding caching.

**Azure is switched on by the user after this stage is built.** Everything here must build, run and test green with no Azure credentials, and turning Azure on must be a configuration change only.

## Configuration and provider selection

```json
"Embeddings": {
  "Provider": "Fake",
  "Model": "text-embedding-3-small",
  "Endpoint": "",
  "Deployment": "",
  "ApiKey": "",
  "MaxBatchSize": 512
}
```

- `EmbeddingOptions` lives in `Lumen.Infrastructure/DependencyInjection/` (same convention as `JwtOptions`/`ChunkingOptions`), bound in `Lumen.Api`'s `Configure()`.
- `Provider` is `Fake | AzureOpenAI`. `AddLumenServices` registers `IEmbeddingService` through a factory that switches on the bound options, so the extension method keeps its current parameterless signature.
- Options validation runs at startup (`ValidateOnStart`): `AzureOpenAI` requires non-empty `Endpoint` and `Deployment`; an empty `ApiKey` means Microsoft Entra ID (managed identity), which is a valid configuration, not a missing one. `MaxBatchSize` must be 1..2048 (the service's documented array ceiling).
- Defaults: `Fake` in `appsettings.Development.json` and in the integration-test host; the committed `appsettings.json` ships `Provider: "Fake"` with empty Azure fields so a fresh clone runs.

**Dimensions are not configurable.** `EmbeddingConstants.Dimensions = 1536` (in `Lumen.Infrastructure/Constants/`) is the single source of truth for the `vector(1536)` column type, the fake generator's output length, and the `defaultModelDimensions` argument passed to Azure. Changing it means a new migration, which is the truth of the situation rather than a limitation: a config knob that can silently disagree with the column type would fail at insert time with an opaque Postgres error. `text-embedding-3-large` remains usable — it shortens to 1536 through the same parameter.

**Azure client construction** (research file 1 for sources): the plain `OpenAI` package, no `Azure.AI.OpenAI`.

```
OpenAIClient(endpoint = "https://<resource>.openai.azure.com/openai/v1/", key or BearerTokenPolicy+DefaultAzureCredential)
  → .GetEmbeddingClient(<deployment>)
  → .AsIEmbeddingGenerator(EmbeddingConstants.Dimensions)
```

Rationale: `Azure.AI.OpenAI`'s newest *stable* release (2.1.0, 2024-12-06) was built against `OpenAI 2.1.0`, while `Microsoft.Extensions.AI.OpenAI 10.10.0` requires `OpenAI >= 2.13.0`; everything in between on the Azure package is beta. Azure's v1 API (GA since August 2025) removes the need for the Azure-specific client entirely, including automatic Entra token refresh — which is also the Stage 6 managed-identity story.

## Domain and schema

- `Lumen.Domain.Entities.DocumentChunk` gains `float[] Embedding` and `string EmbeddingModel`. Domain keeps zero external dependencies — `float[]` is BCL, `Pgvector.Vector` never crosses into Domain or Application.
- `DocumentChunkEntity` gains `Pgvector.Vector Embedding` and `string EmbeddingModel`; the `float[]` ↔ `Vector` conversion lives in the existing `DomainEntityMappingProfile`.
- `DocumentChunkEntityConfiguration`: `Embedding` → `HasColumnType("vector(1536)")`, required; `EmbeddingModel` → `HasMaxLength(100)`, required; a b-tree index on `(TenantId, EmbeddingModel)` — the "index the filter column" advice from pgvector's own README, which is what makes exact search with a tenant predicate cheap.
- `AppDbContext.OnModelCreating` gains `modelBuilder.HasPostgresExtension("vector")`; the `AddDbContext` call gains `o.UseVector()`. Both are required — the first makes the migration emit `CREATE EXTENSION IF NOT EXISTS vector`, the second registers the type mapping.
- New migration `AddChunkEmbeddings`: extension + two columns + index. **It contains no data-destroying SQL.** On a non-empty `DocumentChunks` the `NOT NULL` column cannot be added, and the documented procedure is to recreate the local dev database (`docker compose down -v && docker compose up -d`; the seeder repopulates tenants/users) and re-upload the filings. Chunks are derived data; deleting them silently from inside a migration while leaving their `Documents` rows and raw files behind would create a worse, quieter inconsistency.
- `docker-compose.yml` and `PostgresFixture` move from `postgres:17-alpine` to `pgvector/pgvector:pg17`. Same Postgres major version, plus the extension.

**Why `NOT NULL` plus a model column** rather than a nullable vector: the upload pipeline's invariant is that a persisted chunk always has a vector (see below), so the schema should say so. `EmbeddingModel` exists because the same table will, over this stage's life, see vectors from the fake generator and then from Azure; a search that mixes them returns confident nonsense. Filtering the query by the currently configured model means stale rows drop out of the results instead of poisoning them.

## Embedding seam

`Lumen.Application/Interfaces/IEmbeddingService.cs`:

```csharp
public interface IEmbeddingService
{
    string ModelName { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
```

Application and Domain depend on nothing from the AI stack — the same treatment `IDocumentReader`, `IChunker` and `IFileStorage` already get. Two implementations in `Lumen.Infrastructure/Embeddings/`:

- **`AzureOpenAIEmbeddingService`** — adapter over `IEmbeddingGenerator<string, Embedding<float>>`. Splits the input into batches of `MaxBatchSize`, calls sequentially (a parallel fan-out only buys latency at the price of the TPM quota), preserves input order, returns one `float[]` per input. `ModelName` is the configured `Model`.
- **`FakeEmbeddingService`** — derives each vector deterministically from a hash of the input text, normalized to unit length, of `EmbeddingConstants.Dimensions` length, no network. `ModelName` is `"fake-1536"`. Deterministic rather than random on purpose: it lets integration tests assert real ranking behaviour (the chunk whose text equals the query must rank first, at distance 0) instead of merely asserting that some row came back.

The fake lives in Infrastructure, not in the test projects, because the user runs the app without Azure and needs the whole pipeline to work.

## Upload pipeline

`UploadDocumentCommandHandler` becomes: reader → chunker → **`EmbedAsync(chunk texts)`** → assign `Embedding` and `EmbeddingModel` to every chunk → save the raw file → one `CompleteAsync`.

- **All-or-nothing.** If the provider throws, nothing is persisted: no `Document` row, no chunks, no raw file. The embedding call is placed *before* the raw-file write specifically so a provider failure cannot orphan a file on disk. (The pre-existing orphan-on-`CompleteAsync`-failure window from Stage 2 is unchanged and stays out of scope.)
- **Error mapping.** A new `Lumen.Application/Exceptions/ExternalServiceException` maps to **503** in `GlobalExceptionHandler`. The response body carries a generic message; the endpoint, status code and provider detail go to NLog only — the upload response must never echo configuration back to the caller.
- Upload latency now includes the embedding round-trip. Expected shape from Stage 2's numbers: 296 chunks ≈ 60k tokens ≈ one batch call at `MaxBatchSize` 512. The real number is a Stage 3 measurement for `DECISIONS.md`.

## Search

**`POST /api/search`** — `[Authorize]`, both roles (`Admin` and `Member`; querying is what `Member` exists for). Request `{ query, topK, documentId? }`: `query` required and non-blank (400 otherwise), `topK` defaults to 5 and is capped at 50, `documentId` optional and scopes the search to one filing. Response: a list of `{ chunkId, documentId, documentTitle, chunkIndex, chunkType, tokenCount, text, distance }`.

Flow: `SearchChunksQuery` → handler embeds the query text in one call → `IChunkSearchRepository.SearchAsync(vector, topK, documentId, ct)` (`Lumen.Application/Interfaces`, implemented in `Lumen.Infrastructure/Repositories/ChunkSearchRepository.cs`) runs hand-written SQL:

```sql
SELECT c."Id", c."DocumentId", c."ChunkIndex", c."Text", c."TokenCount", c."ChunkType",
       d."Title" AS "DocumentTitle",
       c."Embedding" <=> @query AS "Distance"
FROM "DocumentChunks" c
JOIN "Documents" d ON d."Id" = c."DocumentId" AND d."TenantId" = @tenantId
WHERE c."TenantId" = @tenantId
  AND c."EmbeddingModel" = @model
  AND (@documentId IS NULL OR c."DocumentId" = @documentId)
ORDER BY "Distance"
LIMIT @topK
```

- Cosine distance (`<=>`), exact search, no index hint — the project plan's "exact search first".
- `@tenantId` comes from `ICurrentTenant` inside the repository, never from client input, and sits **before** `ORDER BY`: filtering happens before ranking. Filtering after ranking can return fewer than `topK` rows, or none, when another tenant's vectors dominate the top of the global ordering.
- `@model` is `IEmbeddingService.ModelName`, so rows embedded by a different model are invisible rather than misleading.
- The `d."TenantId" = @tenantId` on the join is redundant with the `WHERE` in a correct database and is kept as defence in depth.
- Execution: EF Core's unmapped `Database.SqlQueryRaw<T>` with `NpgsqlParameter`s, the vector passed as a `Pgvector.Vector`. Projecting into a type that is not in the EF model has been supported since EF Core 8.0 — "The type used must have a property for every value in the result set, but it doesn't need to match any table in the database - it can, for example, represent a subset of columns, or the result of a join across multiple tables" ([SQL Queries — EF Core, "Querying unmapped types"](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries), updated 2026-09-03), which is exactly the shape here (chunk columns + the joined document title + the computed distance). This path carries no global query filter *by construction* (the result is a projection, not a mapped entity, and the query's `ORDER BY … LIMIT` shape is non-composable anyway) — which is exactly what makes the isolation test meaningful. Escape hatch if the raw path's type mapping misbehaves: pass the vector as text and cast in SQL (`@query::vector`); pgvector accepts its own text representation.
- Results map Domain → Dto in the controller, per the project's DTO rule. The Application layer returns a Domain-side `ChunkSearchResult` (chunk + document title + distance) in a new `Lumen.Domain/Models/` folder — it is a read model, not an entity, and does not belong in `Entities/`.

## Testing

**Unit** (no DB, no network):
- `FakeEmbeddingService`: same text → identical vector; vector length equals `EmbeddingConstants.Dimensions`; different texts → different vectors; unit length.
- `AzureOpenAIEmbeddingService` against a stub `IEmbeddingGenerator`: N inputs with N > `MaxBatchSize` produce `ceil(N/B)` calls, order preserved, one vector per input.
- `UploadDocumentCommandHandler`: every persisted chunk carries a vector and the service's `ModelName`; when the embedding service throws, `CompleteAsync` is never called and no file is written.
- `SearchChunksQueryHandler`: the query text is embedded exactly once; `topK`/`documentId` reach the repository; blank query → `ValidationException`.

**Integration** (Testcontainers on `pgvector/pgvector:pg17` + `WebApplicationFactory`):
- The migration creates the extension and a `vector(1536)` column.
- Upload → every chunk row has a non-null vector and the expected model name.
- Searching with the exact text of a known chunk returns that chunk first, at distance ~0.
- **Tenant isolation on the raw SQL — the flagship deliverable.** Two tenants hold chunks with identical text; tenant B's search never returns tenant A's rows. A companion assertion runs the same SQL *without* the tenant predicate and shows that it does leak, so the test is an argument rather than a tautology.
- The `documentId` filter restricts results to one document.
- `DELETE /api/documents/{id}` removes the vectors with the chunks — searching afterwards cannot find them.
- Authorization: anonymous → 401; `Member` can search successfully.

## Deferred / open

- **ANN index (HNSW).** Not in Stage 3. When it comes, pgvector's own README warns that a shared approximate index across tenants degrades recall per tenant and that filters are applied *after* the index scan — so the Stage 4 decision is between iterative index scans (`hnsw.iterative_scan`), partial indexes, and partitioning, measured rather than assumed. The `SET LOCAL hnsw.ef_search = …` tuning that this project cites as the justification for hand-written SQL becomes real at that point.
- **Embedding cache** (`UseDistributedCache` from `Microsoft.Extensions.AI`): revisit if the user's parameter sweeps hit the TPM quota. Cost is not the argument — a full Apple 10-K re-embed is ≈ $0.0012.
- **Re-index endpoint** over the stored raw file: dropped from this stage; re-upload covers the same need for now.
- **Stage 2 leftovers that this stage does not fix**: approximate `CharStart`/`CharEnd`, no repeated header row when an oversized table is split, dropped empty table cells, the uncalibrated PDF heuristic, and the still-unchosen generic handling of split `$`/`%` table cells. Stage 3 makes their effect *visible* (bad chunks surface as bad search results) without changing them.
- **Measurements this stage must produce for `DECISIONS.md`**: upload time with embedding vs Stage 2's 0.75 s, search latency at the corpus size reached, and — once Azure is on — whether 250/60 chunking survives contact with real retrieval.
