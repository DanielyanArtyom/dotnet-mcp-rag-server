# pgvector + .NET: storage, schema, query

Sources current as of **2026-09-20**. Scope: the storage half of Stage 3 — the extension's own limits and operators, the .NET packages that speak `vector`, how the column reaches an EF Core migration, and which Docker image carries the extension. The client half is `embeddings-dotnet-and-azure-openai.md`; the tenant-isolation argument for raw SQL was settled earlier in `.research/ef-core-raw-sql-tenant-isolation.md` and is only extended here.

## pgvector facts that constrain the design

All from the extension's own README (version **0.8.6**). [pgvector/pgvector README](https://github.com/pgvector/pgvector/blob/master/README.md).

**Types and dimension ceilings — two different numbers, easy to conflate:**

| Type | Storable dimensions | **Indexable** dimensions | Storage per row |
|---|---|---|---|
| `vector` (float32) | up to 16,000 | **up to 2,000** | `4 × dims + 8` bytes |
| `halfvec` (float16) | up to 16,000 | up to 4,000 | `2 × dims + 8` bytes |
| `bit` | — | up to 64,000 | `dims / 8 + 8` bytes |

The README's FAQ is explicit: "What if I want to index vectors with more than 2,000 dimensions? You can use half-precision vectors or half-precision indexing to index up to 4,000 dimensions or binary quantization to index up to 64,000 dimensions." So `text-embedding-3-large` (3,072) stores fine but cannot take an HNSW/IVFFlat index on a plain `vector` column. At 1,536 dimensions a row's vector costs `4 × 1536 + 8` = **6,152 bytes**; Stage 2's 296-chunk Apple filing is therefore ~1.8 MB of vector data.

**Distance operators and their opclasses:**

| Operator | Distance | Index opclass |
|---|---|---|
| `<->` | L2 / Euclidean | `vector_l2_ops` |
| `<=>` | cosine | `vector_cosine_ops` |
| `<#>` | negative inner product | `vector_ip_ops` |

`<#>` returns the *negative* inner product "since Postgres only supports `ASC` order index scans on operators."

**Exact search needs no index at all:** `SELECT * FROM items ORDER BY embedding <-> '[3,1,2]' LIMIT 5;` — perfect recall, full table scan. This is exactly Stage 3's scope (the 7-stage plan says "exact search first (no ANN index)").

**HNSW, for when Stage 4 needs it:** build parameters `m` (default 16) and `ef_construction` (default 64); query-time `hnsw.ef_search` (default 40), settable per transaction:

```sql
BEGIN;
SET LOCAL hnsw.ef_search = 100;
SELECT ...
COMMIT;
```

This `SET LOCAL` is the stated justification in `.research/ef-core-raw-sql-tenant-isolation.md` for hand-writing the vector query rather than using a LINQ API — it stays true: no LINQ vector surface exposes session GUCs.

**Filtering + ANN is a real trap, and it is the multi-tenant case:** "With approximate indexes, filtering is applied *after* the index is scanned. If a condition matches 10% of rows, with HNSW and the default `hnsw.ef_search` of 40, only 4 rows will match on average." Mitigations the README names: a b-tree index on the filter column (exact search is often fine and is what Stage 3 does anyway), **iterative index scans** (`SET hnsw.iterative_scan = strict_order | relaxed_order`, available since 0.8.0), partial indexes per value, or partitioning. Its **Multitenancy** section is blunt: "sharing an approximate index between tenants means vectors from one tenant can affect recall (and speed) for other tenants. For tenant isolation, use list partitioning or separate tables."

→ Stage 3 (exact search) is unaffected; this is a Stage 4 decision to record, not to pre-solve. But it means the eventual HNSW index is *not* a drop-in: a tenant-filtered ANN query needs either iterative scans or partitioning, and recall must be measured, not assumed.

## .NET packages

**Npgsql's EF Core 10 provider has no native vector support.** The 10.0 release notes cover JSON complex types, scalar collections, PostgreSQL 18 virtual generated columns, UUIDv7 and the `cube` extension — no pgvector, no `IsVectorProperty`/`IsVectorIndex`/`VectorDistance`. [Npgsql EF Core 10.0 release notes](https://www.npgsql.org/efcore/release-notes/10.0.html). (EF Core 10's own `IsVectorProperty`/`IsVectorIndex` APIs are the SQL Server provider's vector-search feature — cross-checked against `.research/ef-core-raw-sql-tenant-isolation.md`, which already recorded that the *SQL Server* provider ships LINQ vector search while Postgres does not.)

So the community packages do the work. [pgvector/pgvector-dotnet](https://github.com/pgvector/pgvector-dotnet):

| Package | Latest | Purpose |
|---|---|---|
| `Pgvector` | **0.3.2** | The `Vector`/`HalfVector`/`SparseVector` types + Npgsql type mapping. |
| `Pgvector.EntityFrameworkCore` | **0.3.0** (2025-12-21) | EF Core integration. Targets net8.0+, works with **EF Core 9 and 10**; depends on `Npgsql.EntityFrameworkCore.PostgreSQL >= 9.0.1` and `Pgvector >= 0.3.2`. [NuGet](https://www.nuget.org/packages/Pgvector.EntityFrameworkCore) |
| `Pgvector.Dapper` | — | `SqlMapper.AddTypeHandler(new VectorTypeHandler())` for Dapper. |

Lumen currently resolves `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3`, which satisfies that floor.

**Wiring (two places, both required):**

```csharp
// OnModelCreating
modelBuilder.HasPostgresExtension("vector");

// OnConfiguring / AddDbContext
optionsBuilder.UseNpgsql(connString, o => o.UseVector());
```

`HasPostgresExtension` is what makes `dotnet ef migrations add` emit `CREATE EXTENSION IF NOT EXISTS vector` — the extension does not appear by magic, and a migration run against a stock `postgres:17-alpine` image will fail at that statement.

**Column declaration** (dimensions are part of the type, not a separate facet):

```csharp
[Column(TypeName = "vector(3)")]
public Vector? Embedding { get; set; }
```

The fluent equivalent is `.HasColumnType("vector(1536)")`, which is the form that fits Lumen's `IEntityTypeConfiguration<T>` convention.

**LINQ distance query** (filter-respecting, but composes to `ORDER BY … LIMIT`, i.e. the shape EF cannot wrap — see the isolation file):

```csharp
var items = await ctx.Items
    .OrderBy(x => x.Embedding!.L2Distance(embedding))
    .Take(5)
    .ToListAsync();
```

Available: `L2Distance`, `CosineDistance`, `MaxInnerProduct`, `L1Distance`, `HammingDistance`, `JaccardDistance`.

**HNSW index in the model** (Stage 4, kept here so the option is documented):

```csharp
modelBuilder.Entity<Item>()
    .HasIndex(i => i.Embedding)
    .HasMethod("hnsw")
    .HasOperators("vector_cosine_ops")
    .HasStorageParameter("m", 16)
    .HasStorageParameter("ef_construction", 64);
```

**Raw path (what the tenant-isolation test targets).** `Pgvector` registers the type mapping on a data source (`new NpgsqlDataSourceBuilder(connString).UseVector()`), a `Vector` goes in as an ordinary parameter (`cmd.Parameters.AddWithValue(new Vector(floats))`), and results read back through a normal `DbDataReader`. With Dapper the same works after `SqlMapper.AddTypeHandler(new VectorTypeHandler())`. Either way the SQL is hand-written, so **`WHERE "TenantId" = @tenantId` must be written by hand and placed before `ORDER BY`** — filtering before ranking, per the earlier research file.

## Docker image

The stock `postgres:17-alpine` in `docker-compose.yml` and in `PostgresFixture` does **not** ship the extension. The official image that does is `pgvector/pgvector`, tagged by Postgres major version; `pg16`, `pg17` and `pg18` all exist, plus version-pinned forms — `0.8.6-pg17`, `0.8.6-pg17-bookworm`, `0.8.6-pg17-trixie`. [Docker Hub: pgvector/pgvector](https://hub.docker.com/r/pgvector/pgvector) (tag list read from the Docker Hub API, 2026-09-20). Staying on Postgres 17 keeps parity with what the project already runs; `pgvector/pgvector:pg17` is the drop-in, and `0.8.6-pg17` is the pinned form if reproducibility matters more than staying current.

Testcontainers for .NET takes it through the existing builder: `new PostgreSqlBuilder().WithImage("pgvector/pgvector:pg17")…` — no separate module needed (Testcontainers publishes a pgvector module, but for .NET the image swap on `PostgreSqlBuilder` is the documented-by-example route). [Testcontainers pgvector module](https://testcontainers.com/modules/pgvector/).

## Open / unverified

- Whether Azure Database for PostgreSQL flexible server (a Stage 6 concern) has pgvector 0.8.x available as an allow-listed extension at the tier this project would use — not checked; irrelevant until deployment.
- Recall/latency of exact search on this corpus size is unmeasured. With ~300 chunks per filing and a handful of filings, a sequential scan over a few thousand rows should be sub-millisecond-to-low-millisecond — but that is an expectation, not a measurement, and Stage 3 should produce the real number for `DECISIONS.md`.
