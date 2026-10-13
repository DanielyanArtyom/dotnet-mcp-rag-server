# DECISIONS.md

Interview notes. One entry per real decision, added as each stage completes, 3–6 lines, no exceptions: if a decision can't be backed by a number yet, it's too early to write it down — run the eval or the test first, then write.

Entries are kept in chronological order, which is also stage order. Written in English only.

## Template

```
## [Date] <The decision in one line>

**Context:** <the situation that called for a decision>

**Options:** <what was considered>

**Decision:** <what was chosen>

**Why:** <the reason, with a concrete number — % accuracy, ms, MB, anything measurable>

**Would revisit when:** <which change in circumstances would reopen this>
```

<!-- Entries go below, by stage. Only real decisions with real numbers — no examples or placeholders. -->

## Index

**Before Stage 1 / Stage 1 — skeleton**
- [PostgreSQL + pgvector instead of SQL Server 2025](#2026-09-09-postgresql--pgvector-instead-of-sql-server-2025) — pgvector's HNSW is GA while SQL Server's ANN index was preview; native arm64 image
- [Exactly one legal `.IgnoreQueryFilters()`](#2026-09-10-tenant-isolation-via-an-ef-core-global-query-filter--exactly-one-legal-ignorequeryfilters) — 1 call site, 2 independent isolation tests

**Stage 2 — ingest & chunking**
- [Atomic table chunks, prose at 250/60 tokens](#2026-09-18-tables-from-a-filing-become-atomic-chunks-prose-is-split-by-a-25060-token-budget) — 29 % of chunks are tables a fixed splitter would cut mid-row

**Stage 3 — embeddings & search**
- [Azure OpenAI via the plain `OpenAI` package and the v1 API](#2026-09-20-azure-openai-through-the-plain-openai-package-and-the-v1-api-without-azureaiopenai) — avoids 12 minor versions of assembly skew
- [Vector search as raw SQL with an explicit tenant predicate](#2026-09-20-vector-search-is-raw-sql-with-an-explicit-tenant-predicate-isolation-is-proven-by-a-test-with-a-negative-control) — a negative control shows the same SQL leaks without the predicate

**Stage 4 — RAG & eval-driven tuning**
- [`gpt-4o` answers, `gpt-5-mini` judges](#2026-10-05-models-for-apiask-gpt-4o-answers-gpt-5-mini-judges--chosen-by-quota-not-quality) — chosen by quota: 0 TPM on every newer model except `gpt-5-mini`
- [Ingest profile stored on every document](#2026-10-06-chunking-parameters-are-stored-on-the-document-ingest-profile-the-harness-checks-the-corpus-before-a-run) — a mixed corpus had pushed hit@5 down to 52 % (71 % once re-chunked)
- [Per-document chunk cap off](#2026-10-07-the-at-most-3-chunks-per-document-cap-is-turned-off) — accuracy 70 % → 63 %, MRR worse at p = 0.033
- [10 sources instead of 5](#2026-10-07-context-for-the-model-10-chunks-instead-of-5-chatcontexttopk--10) — accuracy 70 % → 83 %, 4:0 flips
- [Hybrid search off](#2026-10-07-hybrid-search-postgres-fts--rrf-stays-off--it-bought-nothing-on-this-corpus) — 3:3 flips, p = 1.0
- [Short-chunk merging off](#2026-10-07-short-chunk-merging-mergebelowtokens-stays-off) — 1:1 flips
- [Context headers on](#2026-10-07-a-context-header-on-every-chunk-stays-on-chunkingcontextheaders--true) — hit@10 67 % → 92 %
- [Decimal-safe chunking on](#2026-10-07-whole-decimal-numbers-during-chunking-stay-on-chunkingprotectdecimals--true) — the only significant gain, MRR p = 0.0085
- [Temperature is not a lever](#2026-10-07-temperature-02-is-not-a-quality-lever-on-this-task-e5) — 87 % at 0, 0.2 and 0.6
- [Prompt and exact search left alone](#2026-10-07-the-prompt-and-the-vector-index-stay-as-they-are-e6-e7-not-needed) — 0 % false answers in 15 runs; exact search ~300 ms

**Stage 5 — MCP**
- [Stateless transport, required for isolation](#2026-10-11-the-mcp-servers-transport-is-stateless-because-tenant-isolation-depends-on-it) — stateful mode saw an empty tenant
- [Search-only tools, no `ask`](#2026-10-11-mcp-exposes-search-only-no-server-side-ask-tool) — 87 % = 87 %, at ~9× the cost
- [`get_chunk_context` off](#2026-10-11-the-neighbour-chunk-tool-get_chunk_context-stays-switched-off) — 0 calls in 30 questions
- [Existing JWT + token helper, not OAuth](#2026-10-11-mcp-clients-authenticate-with-the-existing-jwt-through-a-token-helper-not-oauth) — zero new auth code; 401 → helper → retry verified
- [Isolated agent eval](#2026-10-11-agent-evaluation-runs-the-client-in-an-isolated-configuration) — 40 % → 87 % once the user's own settings were kept out

**Stage 6 — run anywhere & tables**
- [Split tables keep header, row group and caption](#2026-10-12-split-tables-keep-their-header-row-group-and-caption-tablecells-tableheaders-tablecaptions--true) — accuracy 77 % → 80 %, hit@3 52 % → 62 %
- [Key-value table format off](#2026-10-12-the-key-value-table-format-stays-off-tableformat--pipe) — 0:1 flips, +58 % 10-Q chunks
- [No context header in the `/api/ask` prompt](#2026-10-12-the-context-header-is-not-added-to-the-apiask-prompt-chatsourcecontextheader--false) — 0:0 flips
- [`aspnet:10.0-noble-chiseled-extra`, non-root](#2026-10-12-the-container-runs-on-aspnet100-noble-chiseled-extra-non-root) — 202 MB vs 294 MB
- [Migrate and seed at startup behind flags](#2026-10-12-migrations-and-demo-seeding-run-at-startup-behind-flags-not-as-a-separate-migration-job) — cold start 7.5 s, warm 1.2 s

## 2026-09-09 PostgreSQL + pgvector instead of SQL Server 2025

**Context:** before Stage 1 we needed one database for both the tenants' relational data and the vectors. For a .NET project the natural candidate is SQL Server 2025 with its native `VECTOR` type. Development happens on an Apple Silicon Mac in Docker; the deployment target is Azure.

**Options:** (a) SQL Server 2025 (`VECTOR` + `VECTOR_DISTANCE`); (b) PostgreSQL + pgvector; (c) a dedicated vector store (Qdrant) or Azure AI Search next to a relational database.

**Decision:** (b). Option (c) was dropped because a second store means enforcing tenant isolation in two places.

**Why:** as of 2026-09-09, SQL Server 2025 had exact search (`VECTOR_DISTANCE`) GA, but its ANN index (`CREATE VECTOR INDEX`, DiskANN) and `VECTOR_SEARCH` were still preview behind the `PREVIEW_FEATURES` flag (Microsoft docs updated 2026-08-25), with a 1,998-dimension limit. Its Docker image is x86-64 only, and the RTM image crashes under emulation on Apple Silicon (`x86_avx_state_ptr` assertion); a CU1 fix is reported only by a third-party blog. pgvector is a stable v0.8.6 with HNSW/IVFFlat GA (not preview), and `pgvector/pgvector:pg17` runs natively on arm64. What the project needs to show — raw SQL that bypasses EF Core's global filter, with an isolation test on top — works equally well on both, so the deciding factor was operational, not capability.

**Would revisit when:** DiskANN indexing in SQL Server goes GA and a native arm64 image ships, or the project needs to be shown on a ".NET + SQL Server" stack (e.g. Azure SQL Database instead of Azure Database for PostgreSQL in a real Azure deploy).

## 2026-09-10 Tenant isolation via an EF Core global query filter + exactly one legal `.IgnoreQueryFilters()`

**Context:** Stage 1 required more than compiling multi-tenancy via a `TenantId` discriminator + EF Core global query filter — it had to prove the isolation actually holds, including the login flow, where the tenant isn't known yet (emails are globally unique; the tenant is only known after looking up the user).

**Options:** (a) allow `.IgnoreQueryFilters()` anywhere the code needs to bypass the filter; (b) restrict it to exactly one call site — `UserLookupService.FindByEmailAcrossTenantsAsync` — with an explicit justifying comment, and forbid it everywhere else.

**Decision:** (b). The restriction was checked with grep at every remaining step of Stage 1, from the moment the first legal call appeared.

**Why:** a narrow whitelist makes an isolation breach visible in code review. Final Stage 1 run: 23/23 tests green (11 unit + 12 integration), exactly 1 legal `.IgnoreQueryFilters()` in the codebase, proven by 2 independent tests: `TenantIsolationDbContextTests` (EF level, `AppDbContext` directly) and `DocumentTenantIsolationTests` (full HTTP flow with real JWTs from `POST /api/auth/token`). The schema backs it up: `Documents.TenantId`/`Users.TenantId` are real foreign keys to `Tenants.Id`, and `AppDbContext.SaveChanges` always overwrites `TenantId` on new tenant-owned rows with the current tenant, so a client-supplied value never wins (`TenantScopedWriter` covers legitimate writes outside a request, such as seed data).

**Would revisit when:** a second legitimate cross-tenant scenario appears (for example, a superadmin panel in a later stage) — then add a second explicit, equally narrowly documented call site rather than loosening the rule as a whole.

## 2026-09-18 Tables from a filing become atomic chunks; prose is split by a 250/60 token budget

**Context:** Stage 2 had to turn an SEC filing (dirty HTML, ~1.4 MB) into chunks for future embeddings. A manual check in ChunkViz (1,000 chars / 25% overlap) showed prose splitting fine, but a table (Apple's Segment Operating Performance) was cut mid-row — numbers got separated from their region labels.

**Options:** (a) a single fixed-size character splitter over all text; (b) flatten everything to Markdown and split by headings (the Azure Document Intelligence approach); (c) a typed intermediate model `DocumentElement` (Heading/Paragraph/Table) + `TextChunker` for prose only, each table its own indivisible chunk (as in Unstructured.io).

**Decision:** (c). HTML/PDF → `List<DocumentElement>` (`IDocumentReader`, one implementation per format); the chunker splits only prose (`TextChunker`, 250 tokens / 60 overlap, tokens counted with Tiktoken cl100k); each table is its own `ChunkType.Table`, split by rows only if it exceeds the budget on its own.

**Why:** on a real Apple 10-K FY2025 (1.4 MB HTML → 210,479 characters of text) this produced 296 chunks: 209 prose and 87 tables (29% of chunks are tables that option (a) would have cut mid-row); tokens per chunk: min 4 / median 201 / p95 236 / max 250 (no chunk above the budget — enforced by a test); the whole upload pipeline takes 0.75 s. No counterfactual run of options (a)/(b) on the same data — the choice rests on the manual check above and on these numbers.

**Would revisit when:** the Stage 4 eval set shows failures on table questions (in particular because split large tables don't repeat their header row and `HtmlDocumentReader` drops empty cells), or p95 tokens/chunk drifts far from 250 on bank filings (JPMorgan).

## 2026-09-20 Azure OpenAI through the plain `OpenAI` package and the v1 API, without `Azure.AI.OpenAI`

**Context:** Stage 3 wired embeddings through the `Microsoft.Extensions.AI` abstraction (`IEmbeddingGenerator`), with Azure OpenAI as the concrete provider. The classic route is `AzureOpenAIClient` from the `Azure.AI.OpenAI` package.

**Options:** (a) `Azure.AI.OpenAI` 2.1.0, the latest stable release; (b) an `Azure.AI.OpenAI` 2.x beta; (c) the plain `OpenAIClient` from the `OpenAI` package with `Endpoint = https://<resource>/openai/v1/` — the Azure OpenAI v1 API, GA since August 2025.

**Decision:** (c). One package for the whole stack: embeddings in Stage 3, the chat client for `/api/ask` and the LLM judge in the eval harness in Stage 4.

**Why:** `Microsoft.Extensions.AI.OpenAI` 10.10.0 requires `OpenAI >= 2.13.0`, while `Azure.AI.OpenAI` 2.1.0 (2024-12-06) was built against `OpenAI` 2.1.0. Option (a) would mean running the Azure client on top of an assembly 12 minor versions newer than the one it was compiled against; option (b) means a beta package in the dependency graph. The v1 API was verified live: `text-embedding-3-small` embeddings, and later `gpt-4o` and `gpt-5-mini` chat, work on an AI Foundry resource (`*.services.ai.azure.com/openai/v1/`), not only on `*.openai.azure.com`. Microsoft Entra ID authentication (`BearerTokenPolicy` + `DefaultAzureCredential`) lives in the same client — a ready base for managed identity in a real Azure deploy.

**Would revisit when:** a needed Azure feature is missing from the v1 API's GA subset (e.g. "On Your Data" or deployment-specific parameters), or `Azure.AI.OpenAI` ships a stable release on a current `OpenAI`.

## 2026-09-20 Vector search is raw SQL with an explicit tenant predicate; isolation is proven by a test with a negative control

**Context:** tenant isolation relies on EF Core's global filter (entry of 2026-09-10). The vector query `ORDER BY "Embedding" <=> @query LIMIT k` doesn't compose in LINQ. A composable `FromSqlRaw` on a mapped entity would get the filter applied, but doesn't allow session control (`SET LOCAL hnsw.ef_search`) or combining two search paths in one SQL statement. So it is the one query in the system the filter cannot reach.

**Options:** (a) composable `FromSqlRaw` over `DbSet<DocumentChunk>`, relying on the filter; (b) non-composable `SqlQueryRaw<T>` over an unmapped type with a hand-written tenant predicate; (c) Postgres Row-Level Security.

**Decision:** (b). The tenant predicate comes before `ORDER BY … LIMIT` (`WHERE c."TenantId" = @tenantId`) and again in `JOIN "Documents" d … AND d."TenantId" = @tenantId`; in Stage 4b's hybrid mode it is repeated in every CTE. RLS was not added: it would be a second, parallel isolation mechanism, while the EF Core filter already covers everything else.

**Why:** a "one query bypasses the filter" rule can only be enforced by a test, so the isolation tests are a required deliverable. At the time there were 11 of them in 8 classes at five levels (Stage 5 added MCP-level tests; see the README for the current count): EF Core, HTTP with real JWTs, raw vector SQL, hybrid SQL and `/api/ask` (another tenant's chunks never reach the model). The key test seeds identical chunk text into two tenants, and tenant B's search never returns tenant A's rows. Its paired negative control runs the same query without the predicate and sees both tenants' rows — proving the isolation comes from the predicate, not from the table. `.IgnoreQueryFilters()` in all of `src/` is still exactly 1.

**Would revisit when:** an HNSW index arrives that filters after the scan (pgvector warns about this for multi-tenant tables) — then partition by tenant or use partial indexes. If raw queries grow beyond one or two — then RLS as a second barrier.

## 2026-10-05 Models for `/api/ask`: `gpt-4o` answers, `gpt-5-mini` judges — chosen by quota, not quality

**Context:** Stage 4a needed two chat models on one Azure resource: a full-size one for answers and a **different** judge model to grade prose answers in the eval harness. The research recommended `gpt-6-sol` + a judge from another family, but the subscription is an Azure free trial.

**Options:** (a) `gpt-6-sol` / `gpt-5.4` for answers — both have 0 TPM quota in every deployment type (Global, Data Zone, Standard); (b) `gpt-5-mini` in both roles — the only new model with quota (500K TPM), but then the model grades itself, and it's a reasoning model without `reasoning_effort: none`, i.e. without `temperature`; (c) regional Standard `gpt-4o` (2024-11-20, Sweden Central, 50K TPM) for answers + `gpt-5-mini` (Global Standard) as the judge.

**Decision:** (c). Batch and Provisioned deployment types were ruled out: the first is asynchronous (up to 24 h), the second bills reserved capacity by the hour.

**Why:** on the quota page every model newer than `gpt-4o` shows 0/0 TPM except `gpt-5-mini` (0/500K). `gpt-4o` is not a reasoning model: a `POST /api/ask` probe with `temperature: 0.2` returned 200 (Azure returns 400 on reasoning models); the answer "$416,161 million [2][4]" matched the 10-K, both citations pointed at table chunks with the "Total net sales" row, 1,305 input / 21 output tokens, 0 reasoning tokens. A judge from another family (GPT-5 vs GPT-4o) removes the risk of a model inflating scores for its own answers. 50K TPM is ample: ~2–3K tokens per question, i.e. 15–20 questions a minute.

**Would revisit when:** after upgrading to Pay-As-You-Go and getting `gpt-6-sol` quota approved — switch `Chat:Deployment` and compare runs on the same eval set (`compare`); switch only if the accuracy difference passes McNemar p < 0.05.

## 2026-10-06 Chunking parameters are stored on the document (ingest profile); the harness checks the corpus before a run

**Context:** the first E0 baseline (2026-10-05) showed hit@5 52 %, MRR 0.375, accuracy 57 %. The run header said `chunking 250/60` — it is filled from `GET /api/settings`. The harness flagged 4 questions as unlabelable: their anchors matched no chunk. Querying the database showed max `TokenCount` of 401, 405 and 477 in three of the four documents. Chunks that size can't exist under a 250 budget: the documents were left over from a manual 400/100 experiment in Stage 3 and were never re-uploaded. `/api/settings` truthfully reported the server's config *now*, while the chunks in the database reflected the config *at upload time*.

**Options:** (a) a process rule: "after changing `Chunking`, re-upload everything"; (b) store the chunking parameters on the document itself and check them mechanically.

**Decision:** (b). Every `Document` gets an `IngestProfile` string at upload (e.g. `chunk=250/60;hdr=v1;merge=0;dec=v1`). It is returned by `GET /api/documents`, and the current config's profile by `GET /api/settings`. The harness exits with code 1 on any mismatch, before the first question, and writes the corpus (tenant / document / profile / chunk count) into `run.json`. The same principle was already in place for embeddings since Stage 3: an `EmbeddingModel` column on every chunk + a model filter in the search SQL, plus all-or-nothing upload. Chunking had no equivalent, and that cost an invalid baseline.

**Why:** after re-chunking the whole corpus under 250/60 (and uploading the missing JPM 10-Q), hit@5 went 52 % → 71 %, MRR 0.375 → 0.590, accuracy 57 % → 67 %. Three things changed at once (re-chunking, the extra document, three anchor fixes), so this is the cost of a mixed corpus, not a "400 vs 250" measurement. It was caught because the eval set is labelled with verbatim anchor phrases, not chunk ids. Anchors survive re-chunking (eval set `8a02fdeeef19` went through all 22 Stage 4b runs without an edit, on Apple 10-K's 296 chunks without headers and its 405 with them), so the anchor lint doubled as a "something is wrong with the corpus" detector. All 22 runs since record the corpus and profile in `run.json`, with no mismatches.

**Would revisit when:** a reindex-from-stored-raw-file feature appears. Then the profile turns from a check into a trigger: a document with a stale profile could be re-chunked automatically instead of blocking the run.

## 2026-10-07 The "at most 3 chunks per document" cap is turned off

**Context:** the E1 analysis showed that on 10-Q questions all 5 sources were taken by the same tenant's 10-K. Stage 4b added a soft cap, `Search:MaxChunksPerDocument` (at most M of the top-K from one document; remaining slots are filled past the cap only if no other document has candidates), measured as ladder step b5 on top of b4 (hybrid + headers + short-chunk merging + whole decimals), 30 questions, `gpt-4o`, contextTopK=5.

**Options:** (a) M=3 of 5; (b) no cap.

**Decision:** (b), cap off (`Search:MaxChunksPerDocument = null`).

**Why:** with M=3 accuracy dropped 70 % → 63 % (lost `a-buyback-fy25`, `a-buyback-q3fy26`, `g-eps-2025`, gained 1 question), retrieval MRR 0.572 → 0.558 — permutation test p = 0.033 (the only significant regression on the whole ladder). The sources show the mechanism: the cap pushed the right chunks of the "loud" document out in favour of chunks from the second filing — for `g-eps-2025` the context got a 10-Q table instead of the EPS from the 10-K. "10-Q loses to 10-K" is solved by context depth, not by a quota (see the next entry).

**Would revisit when:** tenants hold many documents of the same type (dozens of filings) and one document starts systematically taking the whole top-K on questions about the others.

## 2026-10-07 Context for the model: 10 chunks instead of 5 (`Chat:ContextTopK = 10`)

**Context:** after the 4b ladder, retrieval finds the right chunk in the top 10 much more often than in the top 5 (at step b4: hit@10 92 % vs hit@5 75 %), yet the model only gets 5 sources and honestly refuses otherwise (0 % false answers at every step). Experiment E4 on the b4 configuration after fixing document dates and titles: same server, same eval set (`8a02fdeeef19`), only `Chat:ContextTopK` changes.

**Options:** (a) 5 sources; (b) 10 sources.

**Decision:** (b), `ContextTopK = 10`.

**Why:** accuracy 70 % → 83 %, false refusals 25 % → 12 %, false answers still 0 %. Flips 4:0 in favour of 10 (`a-buyback-fy25`, `a-china-fy25`, `g-branches`, `g-cet1-delta`), no question lost; McNemar p = 0.125 — the direction is unambiguous, but with n = 30 it doesn't reach 0.05. Cost: input tokens 36.8K → 65.6K per run (+79 %), latency p50 1,046 → 1,414 ms. At ~1,200 input tokens per question that is still a fraction of a cent per answer.

**Would revisit when:** the eval set grows (a larger n is needed to confirm the effect at p < 0.05), or cost/latency becomes a constraint — then try reranking top-10 → top-5 instead of simply widening the context.

## 2026-10-07 Hybrid search (Postgres FTS + RRF) stays off — it bought nothing on this corpus

**Context:** E0/E1 showed tables with numbers losing to prose on the same topic; hybrid search (built-in `english` FTS + cosine, fused with RRF k=60 in one SQL statement with the tenant predicate in each CTE) was the first hypothesis. Measured as ladder step b1 on top of baseline b0 (vector only), 30 questions, `gpt-4o`, contextTopK=5.

**Options:** (a) vector only; (b) hybrid FTS + RRF; (c) a BM25 extension (`pg_textsearch`/ParadeDB) — not tried.

**Decision:** (a). The hybrid code stays behind the `Search:Mode` switch (default `Vector`).

**Why:** accuracy 70 % → 70 %, flips 3:3 (lost `a-net-sales-fy25`, `a-services-fy25`, `g-cet1-q2-26`; gained `a-siri-ai`, `a-dividend-change`, `g-apple-card-allowance`), McNemar p = 1.0; hit@5 71 % → 67 %, MRR 0.590 → 0.550 (p = 0.62). The cost is not zero: ~27 % more tokens go into the context (mean source volume 818 → 1,036), answer p50 924 → 1,175 ms, plus a second raw-SQL path that has to stay under the isolation test. The sources show the mechanism: the built-in `ts_rank_cd` ranking has no IDF, so prose full of common words ("Services net sales include amortization…") rises above the table holding the actual number; the gains on rare words (Siri AI, Apple Card) are eaten by that loss. Confirmed by a control run of the final configuration (vector + headers + whole decimals, contextTopK=10): accuracy 87 % without hybrid and merging vs 83 % with them (E4), flips 2:1, p = 1.0; input tokens 56.8K vs 65.6K, p50 948 vs 1,414 ms.

**Would revisit when:** the eval set gets more questions on exact codes and terms, or with BM25 (which accounts for word rarity) instead of `ts_rank_cd` — that's a separate experiment, not flipping the current switch.

## 2026-10-07 Short-chunk merging (`MergeBelowTokens`) stays off

**Context:** footer chunks and table fragments of 10–20 tokens ("Apple Inc. | 2025 Form 10-K | 56") were landing in the top 5; 19 % of JPM 10-K chunks were like that. Merging chunks ≤ 20 tokens into a neighbour (without exceeding the 250 budget) was measured as ladder step b3 on top of b2.

**Options:** (a) don't merge; (b) merge chunks ≤ 20 tokens.

**Decision:** (a), `Chunking:MergeBelowTokens = 0`.

**Why:** it solved the problem mechanically — on the Apple 10-K chunks ≤ 20 tokens went 7 → 0 (296 → 289 chunks) — but had no effect on quality: accuracy 67 % → 67 %, flips 1:1 (`a-gm-pct-fy25` ↔ `a-buyback-q3fy26`), McNemar p = 1.0; retrieval MRR 0.490 → 0.490 (p = 1.0). The footers turned out not to be the cause of misses — they were being pushed out anyway. Meanwhile merging alters the text of meaningful chunks (a stray footer gets appended to the end of a table) and adds one more ingest step to maintain. The same control run as in the hybrid entry (87 % without hybrid and merging vs 83 % with them) confirms it.

**Would revisit when:** documents arrive with many small fragments that really do take context slots (e.g. PDFs with tables split across pages).

## 2026-10-07 A context header on every chunk stays on (`Chunking:ContextHeaders = true`)

**Context:** a table chunk is row labels and numbers with no table caption, section or document name, and a company's 10-Q and 10-K are nearly indistinguishable by text. A header such as "Apple 10-K (filed 2025-09-27) — Gross Margin" is stored separately from the text (`ContextHeader`) and prepended only for embedding and FTS; the prompt doesn't change. So that each chunk has an unambiguous section, prose is split at section boundaries when headers are on — so this step measures "header + finer chunking" together (Apple 10-K 296 → 405 chunks). Step b2 on top of b1.

**Options:** (a) no headers; (b) headers.

**Decision:** (b).

**Why:** headers trade the top of the ranking for recall: hit@10 67 % → 92 %, but hit@1 46 % → 33 % and MRR 0.550 → 0.490 (p = 0.29), because a shared header makes a document's chunks resemble each other. With 5 sources that bought nothing (accuracy 70 % → 67 %, 3:2, p = 1.0); with 10 sources top-10 recall is what the model sees, and the final configuration (vector + headers + whole decimals, contextTopK=10) reached accuracy 87 %, hit@5 83 %, hit@10 92 %, MRR 0.633 vs baseline b0 at 70 % (7:2, p = 0.18). There is no clean "10 sources without headers" run, so the decision rests on the hit@10 gain, not an isolated effect on accuracy. Side note: fixing dates and one document title moved hit@10 by 9 points with no config change, so ranking is sensitive to header text.

**Would revisit when:** after measuring contextTopK=10 without headers; if accuracy is no worse, remove them (fewer chunks, simpler ingest). Also worth trying a header for FTS only, or with only the section name, without date and document.

## 2026-10-07 Whole decimal numbers during chunking stay on (`Chunking:ProtectDecimals = true`)

**Context:** in 4 % of chunks (148 of 3,768) numbers were broken: "$10. 7 billion", "$19.\n8 billion". The HTML reader was suspected first, but in the stored document text the number is intact — it's broken by `TextChunker.SplitPlainTextLines` inside `Chunker`, which splits long lines at periods, including a period inside a number. Fix: before splitting, a period between digits is replaced with a placeholder character, and restored afterwards. Step b4 on top of b3.

**Options:** (a) leave as is; (b) protect decimals during splitting.

**Decision:** (b).

**Why:** on the Apple 10-K, broken numbers went from 8 of 296 chunks → 0. This is the only ladder step with a statistically significant retrieval improvement: MRR 0.490 → 0.572, permutation test p = 0.0085 (retrieval) and p = 0.032 (full); hit@5 67 % → 75 %; accuracy 67 % → 70 % (2:1, p = 1.0). Cost is zero: no tokens, no latency, one regex in the chunker.

**Would revisit when:** the splitter changes (custom splitting instead of `TextChunker`) — then the protection is no longer needed.

## 2026-10-07 Temperature 0.2 is not a quality lever on this task (E5)

**Context:** `gpt-4o` accepts `temperature` (probe of 2026-10-05), and the stage plan had a separate experiment E5 — how temperature affects accuracy and stability. Final configuration (vector + headers + whole decimals, contextTopK=10), 30 questions, each asked 3 times (90 answers per value).

**Options:** temperature 0, 0.2, 0.6.

**Decision:** keep 0.2 (every measurement of the stage was made with it); the value is the default in `appsettings.json`.

**Why:** accuracy is identical at all three values — 87 %; not a single question changed verdict across the three repeats at either 0 or 0.6 (stability 0 %); false answers 0 % everywhere. The answers are short and anchored to a number from a source — sampling has nowhere to wander. Cost doesn't change either (170K input tokens per 90 answers); p50 976 / 1,065 / 1,127 ms — within noise.

**Would revisit when:** questions needing long explanations appear (e.g. multi-paragraph "why…" answers) or the model changes — especially to a reasoning model, where `ReasoningEffort` is varied instead of temperature.

## 2026-10-07 The prompt and the vector index stay as they are (E6, E7 not needed)

**Context:** the stage plan included E6 (editing the prompt and refusal wording) and E7 (an HNSW ANN index instead of exact search).

**Options:** run the experiments, or close them based on the data from runs already done.

**Decision:** close them without experiments.

**Why:** E6 — 0 % false answers in all 15 full runs of the stage (including 4 questions with no answer in the documents and the cross-tenant traps); every refusal we analysed was caused by the needed fact missing from the context, not by an overly strict prompt — there's nothing to fix. E7 — exact search over ~5,000 chunks across two tenants takes ~300 ms (retrieval-run p50 270–390 ms); HNSW would win milliseconds at the cost of approximate results and separate tuning of the tenant filter (`hnsw.ef_search`, iterative scans).

**Would revisit when:** E6 — if false answers appear or the model changes; E7 — when the corpus grows by an order of magnitude (tens of thousands of chunks per tenant) and search p50 exceeds ~1 s.

## 2026-10-11 The MCP server's transport is stateless, because tenant isolation depends on it

**Context:** Stage 5 exposes search as an MCP server (`ModelContextProtocol.AspNetCore` 2.2.0, Streamable HTTP). The tenant comes from the JWT: `TenantResolutionMiddleware` writes `tenant_id` into a scoped `CurrentTenant`, which the EF global filter and the raw-SQL search both read. The SDK offers stateless and stateful session modes.

**Options:** (a) stateful sessions (server-to-client requests, resumable streams); (b) stateless, every POST independent.

**Decision:** (b), set explicitly (`SessionMode = Stateless`) with a comment that isolation depends on it.

**Why:** reading the SDK source showed that only in stateless mode does a tool run inside the HTTP request's DI scope; in stateful mode each tool call gets a fresh scope from the root provider. Verified with the isolation test: switched to stateful, the tool saw an empty tenant, and the test's positive control (tenant B must find its own document) failed with an empty collection — while a negative-only test ("never returns tenant A's chunk") still passed. So it fails closed rather than leaking, but silently, and a test without a positive control would not notice. All MCP isolation tests now assert both directions. Stateless also needs no sticky sessions behind a load balancer.

**Would revisit when:** a feature needs server-to-client requests (sampling, elicitation) — then the tenant has to be re-resolved inside the tool from the request's claims instead of relying on the shared scope.

## 2026-10-11 MCP exposes search only; no server-side `ask` tool

**Context:** an MCP client is itself a strong model, so the tool surface could be raw retrieval (`search_filings`, `list_filings`; the client writes the answer) or also wrap `/api/ask` (our gpt-4o RAG with citations). Measured with a new harness mode that answers each eval question through `claude -p` + the MCP tools (`claude-sonnet-5`), against the Stage 4 final `/api/ask` run, same 30 questions and corpus.

**Options:** (a) search + list only; (b) only `ask`; (c) both.

**Decision:** (a).

**Why:** the agent with search tools matches the server-side pipeline: accuracy 87 % vs 87 % (3 questions gained, 3 lost, McNemar p = 1.0). It rephrases and searches again when the first query misses (1.2 tool calls per question) — e.g. it found the income-statement table that the first query's top 20 did not contain. Wrapping `/api/ask` would add no accuracy and would nest one model inside another. The trade-offs: ~9× the cost ($1.37 per 30-question run vs about $0.15 of gpt-4o tokens) and ~8× the latency (p50 8.1 s vs 0.95 s); and a weaker refusal discipline — 2 of 6 should-refuse questions got "not disclosed, but here are related figures" instead of the fixed refusal sentence (0 of 6 for `/api/ask`). Neither was a fabrication. Side finding: the agent read the filing title "Morgan 10-K" as Morgan Stanley and refused two JPMorgan questions without searching — for an agent, document metadata is part of the interface.

**Would revisit when:** clients need a guaranteed refusal sentence or cheaper answers (then add `ask_filings` with a description that says when to prefer it), or the client model is weak enough that our pipeline beats it.

## 2026-10-11 The neighbour-chunk tool (`get_chunk_context`) stays switched off

**Context:** split tables lose their header row and caption; a tool returning a chunk's neighbours by `ChunkIndex` was built behind `Mcp:ContextTool` so the agent could fetch them. Measured as M2: the agent run from the previous entry with the tool enabled.

**Options:** (a) expose it; (b) keep it off.

**Decision:** (b), `Mcp:ContextTool = false` by default; the code stays.

**Why:** the agent never called it — 0 calls in 30 questions, accuracy 87 % again, 0 gained / 0 lost against the run without it. With topK = 10 and section headers on every chunk, the search results were enough. A tool the model ignores only adds to the tool list it has to read.

**Would revisit when:** the eval set gets questions whose answer needs rows from both halves of a split table, or the default `topK` is lowered.

## 2026-10-11 MCP clients authenticate with the existing JWT through a token helper, not OAuth

**Context:** the MCP endpoint needed authentication that carries `tenant_id`. The MCP spec's answer for protected HTTP servers is OAuth 2.1 with Protected Resource Metadata; Lumen has a self-issued JWT with a 60-minute lifetime and no authorization server.

**Options:** (a) the same JWT, sent by the client as `Authorization: Bearer`, refreshed by Claude Code's `headersHelper`; (b) long-lived personal access tokens; (c) OAuth 2.1 (OpenIddict or Entra ID) plus PRM.

**Decision:** (a). No `/.well-known/oauth-protected-resource`: without an authorization server it cannot be valid (`authorization_servers` must be non-empty).

**Why:** zero new auth code, and verified live: a helper that hands out an invalid token first makes Claude Code get a 401, re-run the helper and retry — the call succeeds with no user action. `tools/mcp/lumen-token.sh` logs in and prints the header. The cost shows when the header is missing: both Claude Code and MCP Inspector fall back to OAuth discovery and Dynamic Client Registration and get 404s. So the clients are ready for (c), and Claude Desktop / claude.ai — which need OAuth — stay unsupported. PATs (b) would add a token table and a second cross-tenant lookup (`IgnoreQueryFilters`) for a convenience the demo doesn't need.

**Would revisit when:** real users have to connect — then OAuth 2.1 via Entra ID, which also opens Claude Desktop; PATs if users arrive before that.

## 2026-10-11 Agent evaluation runs the client in an isolated configuration

**Context:** the agent eval mode runs `claude -p` per question. The first M1 run scored accuracy 40 % and 100 % false answers — implausible next to `/api/ask`'s 87 % and 0 %.

**Options:** (a) trust the run; (b) read the raw answers and find what differed.

**Decision:** (b). The agent runs with the user's hooks disabled (`--settings '{"disableAllHooks":true}'`) and only project-level settings (`--setting-sources project`, from an empty temporary directory), so the user's own `~/.claude/CLAUDE.md` never reaches it; a refusal counts when the answer contains the refusal sentence.

**Why:** the raw answers were in Russian — the user-level CLAUDE.md overrode the `--append-system-prompt` rule "Always answer in English" — so the number scorer could not read "$416 161 млн" or "46,9 %", and the agent's refusals came with an explanation, which an exact match rejected. `--safe-mode` was tried and rejected: it also disables the MCP server. After the fix the same configuration scored 87 % (40 % → 87 %, nothing else changed). Same lesson as the Stage 4 mixed-corpus baseline: when a metric moves this far, check the environment before the system.

**Would revisit when:** Claude Code changes how settings and memory are loaded, or a different agent client is evaluated — each needs its own isolation check before its numbers count.

## 2026-10-12 Split tables keep their header, row group and caption (`TableCells`, `TableHeaders`, `TableCaptions` = true)

**Context:** a 10-K/10-Q table longer than the 250-token budget was cut into chunks, and every continuation chunk lost its column header: the JPM 10-Q chunk holding `CET1 capital | 14.2 | % | 15.1 | % …` had no years and no "Standardized" header, and its row-group label `Capital ratios - Standardized(a)` ended the previous chunk, so `/api/ask` refused. The caption ("The following table provides…") sat in the preceding prose chunk, and the reader dropped empty cells, which shifted columns. Unstructured and Docling repeat headers only from `<thead>`/`<th>`, which EDGAR HTML never uses, so the header had to be detected from the cell grid (a prototype aligned the columns of 97.8–100 % of the tables with data rows in the four filings). The eval set gained 5 JPM questions whose answer row sits in a continuation chunk (41 questions, n = 35 scored).

**Options:** (a) today's text; (D) colspan grid with `$`/`%` merged into numbers and empty cells kept in position; (D + A′ + B) plus the header rows and the open row-group label repeated in every continuation chunk, and the caption copied into every table chunk; (C) a key-value row format `label | 2025: $34,550 | …`.

**Decision:** D + A′ + B on by default; C stays off (next entry).

**Why:** accuracy 77 % → 80 %, MRR 0.524 → 0.565, hit@3 52 % → 62 %, hit@1 45 % → 48 %, 0 % false answers, at +3–9 % chunks. It fixes the Stage 4 failure `g-cet1-q2-26` (refused → correct) and a new continuation question (CCB employees: not retrieved → rank 2, correct). **Not statistically significant:** 2 gained / 1 lost against the baseline (McNemar p = 1.0; n = 35 needs about 6:0). D alone was 2 lost / 0 gained, both rank swaps between near-identical headerless tables, not lost rows. Costs: a one-row continuation carrying a long repeated header became too similar to its neighbours and left the top 10 (the Stage 4 context-header effect again), and three of the five new questions are not retrieved at any setting. The measurement also caught a bug: a first chunk whose caption plus header overflowed the budget was repacked without its caption (2 such chunks per JPM filing, 63 under C); the fix brought this to 0 and left the ladder's metrics unchanged.

**Would revisit when:** the eval set is large enough for significance. Also when a better header detector for text tables is built: a table whose first numeric row comes late (the Rule 10b5-1 table: `… | 200,000`) gets a 4-row "header", which is too big to repeat, so `g-dimon-10b5-1` (holdout) still refuses. That is a known limitation. Same for tables whose header lives in a separate `<table>` above (143 JPM 10-K tables; not built).

## 2026-10-12 The key-value table format stays off (`TableFormat = Pipe`)

**Context:** with the header detected, every value can carry its column label (`Research and development | 2025: $34,550 | Change: 10% | …`, `Risk-based capital metrics › CET1 capital ratio | Standardized, JPMorgan Chase & Co.: 14.2% | …`). TableRAG and Docling's default serializer use this kind of self-describing cell. Measured on top of the header/caption step.

**Options:** (a) pipe rows with aligned slots; (b) key-value rows.

**Decision:** (a).

**Why:** no gain at a large cost. Accuracy 80 % → 77 % (0 gained / 1 lost, p = 1.0), MRR 0.565 → 0.561, while JPM 10-Q chunks grew 1,444 → 2,280 (+58 %) and the 10-K 2,596 → 3,166. It did move `g-cet1-q2-26` to rank 1, but the answer was already correct at rank 3. Some JPM header spans don't line up with their data columns, so a few key-value labels inherit the wrong period.

**Would revisit when:** retrieval moves to a reranker or a larger context, so that the extra chunks stop competing for 10 slots, or questions appear that need a cell's column named inside the chunk itself, without a header row.

## 2026-10-12 The context header is not added to the `/api/ask` prompt (`Chat:SourceContextHeader = false`)

**Context:** the per-chunk context header ("Morgan 10-K (filed 2025-12-31) — section") reaches embeddings, FTS and MCP results, but `/api/ask` sends the model only the chunk text. The question was whether showing it as a `context="…"` attribute on each `<source>` helps the model; turning it on switches `promptHash` from `ae0bb62cae85`.

**Options:** (a) chunk text only; (b) text plus the context attribute.

**Decision:** (a).

**Why:** zero effect: 0 gained / 0 lost on the header/caption corpus, accuracy 80 % both ways, at about +250 input tokens per question. The table captions already put the needed words into the chunk text.

**Would revisit when:** the prompt or the answering model changes, or questions depend on which filing or section a chunk came from (e.g. 10-K vs 10-Q comparisons, which still fail).

## 2026-10-12 The container runs on `aspnet:10.0-noble-chiseled-extra`, non-root

**Context:** Stage 6A packages Lumen as a container so that `docker compose up` brings up API + Postgres with no .NET SDK installed. The base image decides size, attack surface, whether the process runs as root, and whether ICU is available.

**Options:** (a) `aspnet:10.0` (full Ubuntu, shell, runs as root unless told otherwise); (b) `aspnet:10.0-noble-chiseled` (distroless, non-root, invariant globalization forced); (c) `-chiseled-extra` (distroless and non-root, plus ICU and tzdata).

**Decision:** (c), built with `--platform=$BUILDPLATFORM` and `-a $TARGETARCH` so amd64 and arm64 both build without emulation.

**Why:** the Lumen image is 202 MB on chiseled-extra against 294 MB on the full image (−31 %), and it runs as UID 1654 with no shell or package manager. Plain chiseled would save about 15 MB more (dotnet-docker's size report: 52.8 vs 67.7 MB compressed), but forces invariant globalization. Lumen's own code is culture-invariant, but the code paths inside SemanticKernel's `TextChunker`, AngleSharp and PdfPig are unverified, and a silent change in chunk counts would invalidate every eval baseline. The cost of having no shell: compose can't run an HTTP healthcheck inside the API container (no curl), so only Postgres has a healthcheck, and API health is checked from the host (`/health/ready`, plus `tools/compose-smoke.sh`).

**Would revisit when:** the Apple 10-K characterization test is run once in invariant mode and the chunk count is unchanged; then switch to plain chiseled. Also when a second replica needs an API container healthcheck: then add a `healthcheck` subcommand to the app.

## 2026-10-12 Migrations and demo seeding run at startup behind flags, not as a separate migration job

**Context:** in Production the app had no schema, no users and no data: migrations and seeding ran only in Development. The demo should start with the two tenants and the four EDGAR filings already ingested.

**Options:** (a) an EF migration bundle as a one-shot compose service before the API (Microsoft's recommendation for automated deploys); (b) `MigrateAsync` plus seeding at application startup, behind `Database:MigrateOnStartup`, `Database:SeedDemoData` and `Demo:FilingsPath` (on in Development and compose, off by default).

**Decision:** (b). Filings are seeded through the normal `UploadDocumentCommand`, so a seeded document is identical to an uploaded one (chunks, embeddings, ingest profile, raw file). Every manifest tenant is resolved before the first upload, and already-present titles are skipped.

**Why:** one container and no extra build stage. A cold start including migration and seeding 4,650 chunks with Fake embeddings takes 7.5 s to `/health/ready` (Morgan 10-K, 2,596 chunks, alone takes 3.1 s); a warm restart takes 1.2 s because everything is skipped. Running at startup is safe for more than one instance: with EF Core 9+ on Npgsql, `MigrateAsync` takes `LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE` for the transaction (provider source), so two replicas are serialized rather than racing. Seeding itself has no such lock.

**Would revisit when:** Lumen is deployed for real (Azure App Service): there a migration bundle as a deploy step gives reviewable SQL and lets the app's database user drop DDL rights. Also if seeding with Azure embeddings pushes startup past the platform's start timeout, or if more than one replica seeds at once.
