# SQL Server 2025 vs PostgreSQL+pgvector — vector search comparison

Sources current as of **2026-09-09**. Every claim is tied to a primary source with the page's own last-updated date where visible; unconfirmed claims are flagged explicitly.

## Decision

**Postgres + pgvector**, chosen for this project. See "Which better serves the goal" below and `.research/ef-core-raw-sql-tenant-isolation.md` for the reasoning that made the database choice not matter for the pedagogical goal — the deciding factor ended up being Docker/Apple-Silicon compatibility.

## 1. SQL Server native vector support

**Yes — SQL Server 2025 (17.x) has a native `VECTOR` data type and vector similarity search, GA (not preview) at RTM.**

- `VECTOR` data type (`float32` base), `VECTOR_DISTANCE`, `VECTORPROPERTY` are **fully GA as of SQL Server 2025 RTM**. [SQL Server 2025 Embraces Vectors — Azure SQL Dev Corner](https://devblogs.microsoft.com/azure-sql/sql-server-2025-embraces-vectors-setting-the-foundation-for-empowering-your-data-with-ai/) (published 2025-11-18, coinciding with RTM/GA).
- T-SQL reference confirms: **Applies to: SQL Server 2025 (17.x)**, max 1,998 dimensions, optimized binary storage, exposed as JSON arrays. [Vector Data Type — Microsoft Learn](https://learn.microsoft.com/en-us/sql/t-sql/data-types/vector-data-type?view=sql-server-ver17) (`ms.date` 2026-02-18, updated 2026-04-30).
- **`VECTOR_DISTANCE` (exact/brute-force) is GA.** Approximate nearest-neighbor search — `CREATE VECTOR INDEX` (DiskANN-based) and `VECTOR_SEARCH` — is **still preview**, gated behind `PREVIEW_FEATURES` database config, per the doc's most recent update (2026-08-25). [Vector Search & Vector Index — Microsoft Learn](https://learn.microsoft.com/en-us/sql/sql-server/ai/vectors?view=sql-server-ver17) (`ms.date` 2025-07-24, `updated_at` 2026-08-25).
- Half-precision (`float16`) vectors also still preview-gated.

**Edition availability:** vector features listed `Yes` across Enterprise, Standard, and Express; DiskANN indexing needs the preview flag. Developer edition inherits full Enterprise feature set — **native VECTOR + `VECTOR_DISTANCE` free in Developer edition**. [Editions and Supported Features of SQL Server 2025 — Microsoft Learn](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2025?view=sql-server-ver17) (`ms.date` 2025-11-19, updated 2026-07-20).

**Docker / Linux container:** `mcr.microsoft.com/mssql/server:2025-latest` ships the same engine on Ubuntu 22.04 — full vector support in the container. Documented as supported on **x86-64 only**; no native arm64 image. [Deploy and Connect to SQL Server Linux Containers — Microsoft Learn](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-docker-container-deployment?view=sql-server-ver17).

**Docker on Apple Silicon — a real, documented gotcha:** SQL Server 2025 RTM requires AVX instructions; running the x86_64 container under Docker Desktop emulation on Apple Silicon macOS produced a hard crash (`assertion failed [x86_avx_state_ptr->xsave_header.xfeatures == kSupportedXFeatureBits]`). Workaround at the time: OrbStack (better AVX emulation). A later note claims Microsoft fixed this in **SQL Server 2025 CU1** (noted 2026-02-02). [Getting SQL Server 2025 RTM Running in Containers on macOS — Anthony Nocentino's Blog](https://www.nocentino.com/posts/2025-11-26-sql-server-2025-docker-desktop-avx-issue/) (published 2025-11-26, updated 2026-02-02). Third-party source, not Microsoft — **treat the CU1 fix as plausible but not independently confirmed.**

## 2. Azure SQL Database comparison

Azure SQL Database got native vector support first; by GA date it was the more mature platform, though the gap on ANN/index-based search has narrowed to "both still preview."

| Milestone | Azure SQL Database | SQL Server (box product) |
|---|---|---|
| Public preview of native `VECTOR` type | November 2024 | — |
| GA of `VECTOR`, `VECTOR_DISTANCE`, `VECTOR_NORM`, `VECTOR_NORMALIZE`, TDS binary transport | **2025-06-19** | SQL Server 2025 RTM, **2025-11-18** |
| DiskANN vector indexing / `VECTOR_SEARCH` | Private preview (June 2025) | Public preview (still preview per 2026-08-25 doc update) |

Sources: [Announcing GA of Native Vector Type & Functions in Azure SQL](https://devblogs.microsoft.com/azure-sql/announcing-general-availability-of-native-vector-type-functions-in-azure-sql/) (2025-06-19); [SQL Server 2025 Embraces Vectors](https://devblogs.microsoft.com/azure-sql/sql-server-2025-embraces-vectors-setting-the-foundation-for-empowering-your-data-with-ai/) (2025-11-18); [Vector Search & Vector Index — Microsoft Learn](https://learn.microsoft.com/en-us/sql/sql-server/ai/vectors?view=sql-server-ver17) (updated 2026-08-25).

Both platforms share the same T-SQL surface (`VECTOR`, `VECTOR_DISTANCE`, `CREATE VECTOR INDEX`, `VECTOR_SEARCH`); **DiskANN/ANN indexing is currently preview on both** as of the most recent doc update.

**Uncertain / could not verify:** whether DiskANN vector indexing has since reached GA on either platform after 2026-08-25 — no dated GA announcement found. Treat as still preview until reconfirmed at implementation time.

## 3. Alternatives, briefly

- **pgvector** — OSS PostgreSQL extension, exact + approximate (IVFFlat/HNSW) search. Latest tag: **v0.8.6** (2026-07-29). [pgvector/pgvector tags — GitHub](https://github.com/pgvector/pgvector/tags). Mature since ~2021, stable, not preview-gated.
- **SQL Server + external vector index library** — keep relational data in SQL Server, delegate vector indexing to an external library/service instead of SQL Server's own preview DiskANN feature.
- **Azure AI Search** — managed PaaS, vector search + semantic ranker GA since **2023-11-16**. [Announcing GA of vector search and semantic ranker — Microsoft Tech Community](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/announcing-general-availability-of-vector-search-and-semantic-ranker-in-azure-ai/3978525). Long-GA and mature, but a separate Azure service, not a database engine — irrelevant to a local Docker/Mac dev setup unless the project commits to Azure.
- **sqlite-vec** — small, dependency-free SQLite extension (`vec0` virtual table); good for embedded/per-user stores, not shared multi-tenant datasets.
- **Qdrant** — purpose-built OSS/managed vector DB, high-throughput ANN + payload filtering, official Docker image runs natively on x86_64 and arm64 — no Apple Silicon emulation problem.

## 4. Comparison table (SQL Server 2025 vs Postgres+pgvector)

| Factor | SQL Server 2025 | PostgreSQL + pgvector |
|---|---|---|
| Official EF Core provider | `Microsoft.EntityFrameworkCore.SqlServer`, built-in vector LINQ support since EF Core 10 | `Npgsql.EntityFrameworkCore.PostgreSQL` (de facto standard, Npgsql OSS project); vector LINQ via third-party `Pgvector.EntityFrameworkCore` |
| Raw-SQL parameterization | `FromSqlRaw`/`SqlQueryRaw` + `SqlParameter` | Same pattern via `NpgsqlParameter` — provider-agnostic, no real difference |
| Native defense-in-depth (Row-Level Security) | Yes — security policies + predicate functions, available on Enterprise/Standard/Express | Yes — native RLS since Postgres 9.5, per-session `tenant_id` policies |
| Vector feature maturity | `VECTOR`/`VECTOR_DISTANCE` GA at RTM; DiskANN index/`VECTOR_SEARCH` explicitly experimental/preview as of mid/late-2026 | pgvector stable for years (v0.8.6); HNSW/IVFFlat indexing is GA, not preview |
| Docker on Apple Silicon | Documented AVX-emulation crash on 2025 RTM image (workaround: OrbStack); reportedly fixed in CU1 (unconfirmed independently); no native arm64 image regardless | Official `postgres` image + pgvector build natively for arm64, no emulation |
| Licensing for local dev | Free (Developer edition), full Enterprise feature parity | Free, open source, no edition restrictions |

**Which better serves the goal:** both databases support the same underlying mechanism the project wants to demonstrate (see the EF Core file for details) equally well — so **the deciding factor is operational, not pedagogical**: on a Docker-on-Mac workflow, Postgres+pgvector avoids the documented SQL Server/Apple-Silicon container friction entirely and avoids depending on a still-preview ANN indexing feature for the "real" part of the exercise. SQL Server 2025 remains fully legitimate (GA vector type, free Developer edition) and arguably more relevant to a ".NET shop" interview narrative, but currently carries more moving parts incidental to the actual lesson.
