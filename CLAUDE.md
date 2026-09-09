# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

**Pre-implementation.** Only planning/research artifacts exist so far (this file, `README.md`, `.research/`). No solution, no code, no build yet. Stage 1 (below) has not started. Update this file's "Commands" section once a solution exists.

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

## Working with the user

The user writes none of the implementation code themselves — Claude does. But hands-on configuration and experimentation (registering MCP servers in clients, running the eval set, tuning chunking/retrieval parameters, observing what breaks) is done by the **user**, not Claude, because the empirical "tried X, it was bad, understood why, switched to Y" loop is the actual interview material this project produces. At those points in a stage, explain what to try and what signal to watch for, then wait for the user to report back before deciding the next step — don't run the experiment autonomously and just report results.
