# Decisions

My notes on the decisions I made while building Lumen, in the order I made them. I only write an entry once I have a number to back it up. If I can't measure something yet, it waits.

Each entry follows the same shape:

```
## [Date] <The decision in one line>

**Context:** <what made me decide>
**Options:** <what I considered>
**Decision:** <what I picked>
**Why:** <the reason, with a number>
**Would revisit when:** <what would make me reopen it>
```

## Index

**Stage 1: skeleton**
- [PostgreSQL and pgvector instead of SQL Server 2025](#2026-09-09-postgresql-and-pgvector-instead-of-sql-server-2025): pgvector's HNSW is GA, SQL Server's ANN index was preview; native arm64 image
- [One allowed IgnoreQueryFilters call](#2026-09-10-one-allowed-ignorequeryfilters-call): 1 call site, 2 independent isolation tests

**Stage 2: ingest and chunking**
- [Tables are atomic chunks, prose is split at 250/60 tokens](#2026-09-18-tables-are-atomic-chunks-prose-is-split-at-25060-tokens): 29 % of chunks are tables a fixed splitter would cut mid-row

**Stage 3: embeddings and search**
- [Azure OpenAI through the plain OpenAI package](#2026-09-20-azure-openai-through-the-plain-openai-package): avoids 12 minor versions of assembly skew
- [Vector search is raw SQL with an explicit tenant predicate](#2026-09-20-vector-search-is-raw-sql-with-an-explicit-tenant-predicate): a negative control shows the same SQL leaks without the predicate

**Stage 4: RAG and eval-driven tuning**
- [gpt-4o answers, gpt-5-mini judges](#2026-10-05-gpt-4o-answers-gpt-5-mini-judges): picked by quota, 0 TPM on every newer model except gpt-5-mini
- [Every document stores its ingest profile](#2026-10-06-every-document-stores-its-ingest-profile): a mixed corpus had pushed hit@5 down to 52 % (71 % once re-chunked)
- [No per-document chunk cap](#2026-10-07-no-per-document-chunk-cap): accuracy 70 % → 63 %, MRR worse at p = 0.033
- [Ten sources instead of five](#2026-10-07-ten-sources-instead-of-five): accuracy 70 % → 83 %, 4:0
- [Hybrid search stays off](#2026-10-07-hybrid-search-stays-off): 3:3, p = 1.0
- [Short-chunk merging stays off](#2026-10-07-short-chunk-merging-stays-off): 1:1
- [Context headers stay on](#2026-10-07-context-headers-stay-on): hit@10 67 % → 92 %
- [Decimal-safe chunking stays on](#2026-10-07-decimal-safe-chunking-stays-on): the only significant gain, MRR p = 0.0085
- [Temperature is not a lever here](#2026-10-07-temperature-is-not-a-lever-here): 87 % at 0, 0.2 and 0.6
- [Prompt and exact search left as they are](#2026-10-07-prompt-and-exact-search-left-as-they-are): 0 % false answers in 15 runs, exact search ~300 ms

**Stage 5: MCP**
- [Stateless MCP transport](#2026-10-11-stateless-mcp-transport): stateful mode saw an empty tenant
- [MCP exposes search only](#2026-10-11-mcp-exposes-search-only): 87 % vs 87 %, at ~9× the cost
- [Neighbour-chunk tool stays off](#2026-10-11-neighbour-chunk-tool-stays-off): 0 calls in 30 questions
- [MCP auth uses the existing JWT, not OAuth](#2026-10-11-mcp-auth-uses-the-existing-jwt-not-oauth): no new auth code; 401 → helper → retry verified
- [The agent eval runs in an isolated configuration](#2026-10-11-the-agent-eval-runs-in-an-isolated-configuration): 40 % → 87 % once my own settings were kept out

**Stage 6: run anywhere and tables**
- [Split tables keep their header, row group and caption](#2026-10-12-split-tables-keep-their-header-row-group-and-caption): accuracy 77 % → 80 %, hit@3 52 % → 62 %
- [Key-value table format stays off](#2026-10-12-key-value-table-format-stays-off): 0:1, +58 % 10-Q chunks
- [No context header in the ask prompt](#2026-10-12-no-context-header-in-the-ask-prompt): 0:0
- [Chiseled-extra base image, non-root](#2026-10-12-chiseled-extra-base-image-non-root): 202 MB vs 294 MB
- [Migrate and seed on startup, behind flags](#2026-10-12-migrate-and-seed-on-startup-behind-flags): cold start 7.5 s, warm 1.2 s

"3:3" and similar numbers mean questions gained : questions lost compared with the previous step.

## 2026-09-09 PostgreSQL and pgvector instead of SQL Server 2025

**Context:** I needed one database for both relational data and vectors. For .NET, SQL Server 2025 with its new `VECTOR` type was the obvious candidate. I develop on an Apple Silicon Mac in Docker, and the target is Azure.

**Options:** SQL Server 2025 (`VECTOR` + `VECTOR_DISTANCE`); PostgreSQL + pgvector; a separate vector store (Qdrant or Azure AI Search) next to a relational database.

**Decision:** PostgreSQL + pgvector. I dropped the separate store early, because it would mean enforcing tenant isolation in two places.

**Why:** as of 2026-09-09, SQL Server 2025 had exact search GA, but its ANN index (DiskANN) and `VECTOR_SEARCH` were still preview behind `PREVIEW_FEATURES`, limited to 1,998 dimensions. Its Docker image is x86-64 only, and the RTM image crashes under emulation on Apple Silicon (`x86_avx_state_ptr` assertion). The only report of a CU1 fix I found was a third-party blog. pgvector 0.8.6 has HNSW and IVFFlat GA, and `pgvector/pgvector:pg17` runs natively on arm64. The thing I wanted to show, raw SQL that bypasses EF Core's filter with a test on top, works on both. So the choice came down to tooling, not features.

**Would revisit when:** DiskANN goes GA in SQL Server and there's a native arm64 image, or I need to show this on a .NET + SQL Server stack (e.g. Azure SQL in a real deploy).

## 2026-09-10 One allowed IgnoreQueryFilters call

**Context:** a `TenantId` column plus an EF Core global filter is easy to set up. I also had to prove it holds, including at login, where the tenant isn't known until the user is found by email (emails are globally unique).

**Options:** let `.IgnoreQueryFilters()` be used wherever needed, or allow exactly one call site (`UserLookupService.FindByEmailAcrossTenantsAsync`) with a comment explaining why.

**Decision:** one call site. I grepped for it at every step of Stage 1 after the first one appeared.

**Why:** with a single allowed place, any new bypass stands out in review. At the end of Stage 1: 23/23 tests green (11 unit, 12 integration) and exactly 1 `.IgnoreQueryFilters()`. Two independent tests cover it: `TenantIsolationDbContextTests` at the EF level and `DocumentTenantIsolationTests` over HTTP with real JWTs. The schema helps too: `Documents.TenantId` and `Users.TenantId` are real foreign keys, and `SaveChanges` always overwrites `TenantId` on new rows with the current tenant, so a value sent by the client never wins. `TenantScopedWriter` handles writes outside a request, like seeding.

**Would revisit when:** a second real cross-tenant case shows up (say, a superadmin panel). Then I'd add a second documented call site instead of relaxing the rule.

## 2026-09-18 Tables are atomic chunks, prose is split at 250/60 tokens

**Context:** I had to turn a 1.4 MB EDGAR HTML filing into chunks. I checked it by hand in ChunkViz (1,000 chars, 25 % overlap). Prose split fine, but Apple's Segment Operating Performance table got cut mid-row, separating the numbers from their region labels.

**Options:** one fixed-size character splitter over everything; flatten to Markdown and split by headings (what Azure Document Intelligence does); a typed model of headings, paragraphs and tables, with `TextChunker` for prose only and every table as its own chunk (like Unstructured.io).

**Decision:** the typed model. Each format has an `IDocumentReader` that produces a `List<DocumentElement>`. The chunker splits prose with `TextChunker` at 250 tokens with 60 overlap (Tiktoken cl100k). A table becomes its own `ChunkType.Table` chunk and is split by rows only if it doesn't fit the budget.

**Why:** the Apple 10-K FY2025 (210,479 characters of text) gave 296 chunks: 209 prose, 87 tables. That's 29 % of chunks that a fixed splitter would have cut mid-row. Tokens per chunk: min 4, median 201, p95 236, max 250, and a test enforces the cap. The whole upload takes 0.75 s. I didn't run the other two options on the same data, so this rests on the manual check and these numbers.

**Would revisit when:** the eval set fails on table questions (split tables don't repeat their header, and the HTML reader drops empty cells), or p95 drifts far from 250 on the JPMorgan filings.

## 2026-09-20 Azure OpenAI through the plain OpenAI package

**Context:** embeddings go through `Microsoft.Extensions.AI` (`IEmbeddingGenerator`), with Azure OpenAI behind it. The usual way in is `AzureOpenAIClient` from `Azure.AI.OpenAI`.

**Options:** `Azure.AI.OpenAI` 2.1.0 (latest stable); an `Azure.AI.OpenAI` 2.x beta; the plain `OpenAIClient` from the `OpenAI` package pointed at `https://<resource>/openai/v1/`, the Azure v1 API that went GA in August 2025.

**Decision:** the plain `OpenAI` client. I use the same package for embeddings, the chat client behind `/api/ask` and the eval judge.

**Why:** `Microsoft.Extensions.AI.OpenAI` 10.10.0 needs `OpenAI >= 2.13.0`, but `Azure.AI.OpenAI` 2.1.0 (2024-12-06) was built against `OpenAI` 2.1.0. That's 12 minor versions of difference under the Azure client, and the alternative was a beta package. I checked the v1 API live: `text-embedding-3-small`, and later `gpt-4o` and `gpt-5-mini`, work on an AI Foundry resource (`*.services.ai.azure.com/openai/v1/`) as well as on `*.openai.azure.com`. Entra ID auth (`BearerTokenPolicy` + `DefaultAzureCredential`) works in the same client, which I'll need for managed identity on Azure.

**Would revisit when:** I need an Azure feature the v1 API doesn't cover (e.g. "On Your Data"), or `Azure.AI.OpenAI` ships a stable release on a current `OpenAI`.

## 2026-09-20 Vector search is raw SQL with an explicit tenant predicate

**Context:** isolation relies on EF Core's global filter (entry of 2026-09-10). The vector query, `ORDER BY "Embedding" <=> @query LIMIT k`, doesn't compose in LINQ. A composable `FromSqlRaw` would get the filter, but I couldn't control the session (`SET LOCAL hnsw.ef_search`) or combine two search paths in one statement. So this is the one query the filter can't reach.

**Options:** composable `FromSqlRaw` over `DbSet<DocumentChunk>` and trust the filter; `SqlQueryRaw<T>` over an unmapped type with the tenant predicate written by hand; Postgres Row-Level Security.

**Decision:** `SqlQueryRaw<T>` with a hand-written predicate. It sits before `ORDER BY … LIMIT` (`WHERE c."TenantId" = @tenantId`) and again in the `JOIN "Documents" d … AND d."TenantId" = @tenantId`. In hybrid mode it's repeated in every CTE. I didn't add RLS. It would be a second isolation mechanism alongside the filter that already covers everything else.

**Why:** a rule like "this one query bypasses the filter" can only be enforced by a test, so the isolation tests were a required deliverable. By then I had 11 of them in 8 classes at five levels: EF Core, HTTP with real JWTs, vector SQL, hybrid SQL and `/api/ask`. Stage 5 added MCP tests; the README has the current count. The main test puts identical chunk text in two tenants and checks tenant B never gets tenant A's rows. Its negative control runs the same query without the predicate and gets both tenants' rows, so the predicate is what isolates the data. `.IgnoreQueryFilters()` in `src/` is still exactly 1.

**Would revisit when:** I add an HNSW index, since it filters after the scan (pgvector warns about this for multi-tenant tables); then I'd partition by tenant or use partial indexes. If raw queries grow past one or two, I'd add RLS as a second barrier.

## 2026-10-05 gpt-4o answers, gpt-5-mini judges

**Context:** Stage 4 needed two chat models on one Azure resource: one to answer, and a different one to judge prose answers in the eval. My research pointed to `gpt-6-sol` plus a judge from another family, but I'm on an Azure free trial.

**Options:** `gpt-6-sol` or `gpt-5.4` for answers, which have 0 TPM quota in every deployment type; `gpt-5-mini` for both roles, the only new model with quota (500K TPM), but then it grades itself, and as a reasoning model it doesn't accept `temperature`; regional Standard `gpt-4o` (2024-11-20, Sweden Central, 50K TPM) for answers with `gpt-5-mini` (Global Standard) as the judge.

**Decision:** `gpt-4o` answers, `gpt-5-mini` judges. I ruled out Batch (async, up to 24 h) and Provisioned (billed hourly for reserved capacity).

**Why:** on the quota page every model newer than `gpt-4o` showed 0/0 TPM except `gpt-5-mini` (0/500K). `gpt-4o` isn't a reasoning model: a probe with `temperature: 0.2` returned 200 (Azure returns 400 for reasoning models). The answer, "$416,161 million [2][4]", matched the 10-K, and both citations pointed at table chunks with the "Total net sales" row. It used 1,305 input and 21 output tokens. A judge from another family means the model doesn't grade its own answers. 50K TPM is plenty at ~2–3K tokens per question, about 15–20 questions a minute.

**Would revisit when:** I move to Pay-As-You-Go and get `gpt-6-sol` quota. Then I'd switch `Chat:Deployment`, compare runs on the same eval set, and keep it only if McNemar gives p < 0.05.

## 2026-10-06 Every document stores its ingest profile

**Context:** the first baseline (2026-10-05) came out at hit@5 52 %, MRR 0.375, accuracy 57 %. The run header said `chunking 250/60`, which it reads from `GET /api/settings`. The harness also flagged 4 questions whose anchor phrases matched no chunk. In the database, three of the four documents had chunks of 401, 405 and 477 tokens, which can't happen with a 250 budget. They were left over from a 400/100 experiment in Stage 3 that I never re-uploaded. `/api/settings` correctly reported the server's config now, while the chunks reflected the config at upload time.

**Options:** a rule to remember ("re-upload everything after changing `Chunking`"), or store the chunking settings on the document and check them automatically.

**Decision:** store and check. Every `Document` gets an `IngestProfile` string at upload, e.g. `chunk=250/60;hdr=v1;merge=0;dec=v1`. `GET /api/documents` returns it, and `GET /api/settings` returns the profile of the current config. The harness exits with code 1 on any mismatch before asking a single question, and writes the corpus (tenant, document, profile, chunk count) to `run.json`. Embeddings already had the same protection since Stage 3 (an `EmbeddingModel` column, a model filter in the SQL, all-or-nothing uploads). Chunking didn't, and it cost me an invalid baseline.

**Why:** after re-chunking everything at 250/60 (and uploading the missing JPM 10-Q), hit@5 went 52 % → 71 %, MRR 0.375 → 0.590, accuracy 57 % → 67 %. Three things changed at once (re-chunking, one more document, three anchor fixes), so this is the cost of a mixed corpus, not a "400 vs 250" result. I caught it because questions are labelled with exact phrases, not chunk ids. Those labels survive re-chunking: eval set `8a02fdeeef19` went through all 22 Stage 4b runs unchanged, on both the 296-chunk and the 405-chunk version of the Apple 10-K. That makes the anchor check a decent "the corpus is off" alarm. None of the 22 runs since then has had a mismatch.

**Would revisit when:** I add re-indexing from the stored raw file. Then a stale profile could trigger re-chunking instead of blocking the run.

## 2026-10-07 No per-document chunk cap

**Context:** on 10-Q questions, all 5 sources often came from the same tenant's 10-K. I added a soft cap, `Search:MaxChunksPerDocument`: at most M of the top-K from one document, with leftover slots filled past the cap only if no other document has candidates. Measured as step b5 on top of b4 (hybrid + headers + merging + decimals), 30 questions, `gpt-4o`, 5 sources.

**Options:** M = 3 of 5, or no cap.

**Decision:** no cap (`Search:MaxChunksPerDocument = null`).

**Why:** with M = 3, accuracy dropped 70 % → 63 %. It lost `a-buyback-fy25`, `a-buyback-q3fy26` and `g-eps-2025` and gained one. MRR went 0.572 → 0.558, permutation test p = 0.033, the only significant regression in the whole ladder. Looking at the sources, the cap pushed the right chunks of the dominant document out in favour of the second filing. For `g-eps-2025` the model got a 10-Q table instead of the EPS from the 10-K. More context fixed the "10-Q loses to 10-K" problem better than a quota (next entry).

**Would revisit when:** tenants hold dozens of filings of the same type and one document keeps taking the whole top-K on questions about the others.

## 2026-10-07 Ten sources instead of five

**Context:** after the 4b ladder, retrieval found the right chunk in the top 10 much more often than in the top 5 (step b4: hit@10 92 % vs hit@5 75 %), but the model only saw 5 sources and refused when the fact wasn't there (0 % false answers at every step). Experiment E4: the b4 configuration after fixing document dates and titles, same eval set (`8a02fdeeef19`), only `Chat:ContextTopK` changes.

**Options:** 5 sources or 10.

**Decision:** 10 (`Chat:ContextTopK = 10`).

**Why:** accuracy 70 % → 83 %, false refusals 25 % → 12 %, false answers still 0 %. 4:0 in favour of 10 (`a-buyback-fy25`, `a-china-fy25`, `g-branches`, `g-cet1-delta`). McNemar p = 0.125, so the direction is clear but n = 30 doesn't reach 0.05. Cost: input tokens per run 36.8K → 65.6K (+79 %), p50 latency 1,046 → 1,414 ms. At ~1,200 input tokens per question, that's still a fraction of a cent per answer.

**Would revisit when:** the eval set grows enough to confirm this at p < 0.05, or cost and latency start to matter. Then I'd try reranking the top 10 down to 5.

## 2026-10-07 Hybrid search stays off

**Context:** E0/E1 showed tables with numbers losing to prose on the same topic. My first idea was hybrid search: Postgres `english` FTS plus cosine, fused with RRF (k = 60) in one statement, with the tenant predicate in each CTE. Measured as step b1 on top of the vector-only baseline b0, 30 questions, `gpt-4o`, 5 sources.

**Options:** vector only; hybrid FTS + RRF; a BM25 extension (`pg_textsearch` or ParadeDB), which I didn't try.

**Decision:** vector only. The hybrid code stays behind `Search:Mode` (default `Vector`).

**Why:** accuracy 70 % → 70 %, 3:3 (lost `a-net-sales-fy25`, `a-services-fy25`, `g-cet1-q2-26`; gained `a-siri-ai`, `a-dividend-change`, `g-apple-card-allowance`), McNemar p = 1.0. hit@5 71 % → 67 %, MRR 0.590 → 0.550 (p = 0.62). It also costs something: ~27 % more context (mean source volume 818 → 1,036), p50 924 → 1,175 ms, and a second raw-SQL path to keep under the isolation tests. The sources explain it: `ts_rank_cd` has no IDF, so prose full of common words ("Services net sales include amortization…") ranks above the table with the actual number. The gains on rare words (Siri AI, Apple Card) are eaten by that. A control run of the final config (vector + headers + decimals, 10 sources) confirmed it: 87 % without hybrid and merging vs 83 % with them (E4), 2:1, p = 1.0, with 56.8K vs 65.6K input tokens and p50 948 vs 1,414 ms.

**Would revisit when:** the eval set has more questions about exact codes and terms, or I try BM25, which accounts for word rarity. That would be a new experiment, not flipping this switch.

## 2026-10-07 Short-chunk merging stays off

**Context:** footer chunks and table fragments of 10–20 tokens ("Apple Inc. | 2025 Form 10-K | 56") were showing up in the top 5, and 19 % of JPM 10-K chunks looked like that. I tried merging chunks of 20 tokens or fewer into a neighbour, without going over 250. Step b3 on top of b2.

**Options:** don't merge, or merge chunks ≤ 20 tokens.

**Decision:** don't merge (`Chunking:MergeBelowTokens = 0`).

**Why:** it did what it was meant to: on the Apple 10-K, chunks ≤ 20 tokens went 7 → 0 (296 → 289 chunks). But quality didn't move: accuracy 67 % → 67 %, 1:1 (`a-gm-pct-fy25` ↔ `a-buyback-q3fy26`), McNemar p = 1.0, MRR 0.490 → 0.490. The footers weren't causing the misses; they got pushed out anyway. Merging also changes useful chunks (a stray footer gets glued onto a table) and adds one more ingest step to maintain. The same control run as in the hybrid entry (87 % vs 83 %) backs this up.

**Would revisit when:** documents come with lots of small fragments that really take up context slots, like PDFs with tables split across pages.

## 2026-10-07 Context headers stay on

**Context:** a table chunk is just row labels and numbers, with no caption, section or document name, and a company's 10-Q and 10-K read almost the same. So I added a header like "Apple 10-K (filed 2025-09-27) — Gross Margin". It's stored separately (`ContextHeader`) and only added for embedding and FTS; the prompt stays the same. To give every chunk a single section, prose is also split at section boundaries when headers are on, so this step measures headers and finer chunking together (Apple 10-K 296 → 405 chunks). Step b2 on top of b1.

**Options:** with or without headers.

**Decision:** with headers (`Chunking:ContextHeaders = true`).

**Why:** headers trade the top of the ranking for recall: hit@10 67 % → 92 %, but hit@1 46 % → 33 % and MRR 0.550 → 0.490 (p = 0.29), because a shared header makes a document's chunks look alike. With 5 sources that gave nothing (accuracy 70 % → 67 %, 3:2, p = 1.0). With 10 sources, the top 10 is what the model sees, and the final config (vector + headers + decimals, 10 sources) reached 87 % accuracy, hit@5 83 %, hit@10 92 %, MRR 0.633, against 70 % at baseline b0 (7:2, p = 0.18). I don't have a clean "10 sources without headers" run, so this rests on the hit@10 gain rather than a measured effect on accuracy. Side note: fixing dates and one document title moved hit@10 by 9 points with no config change, so ranking is sensitive to header text.

**Would revisit when:** I measure 10 sources without headers. If accuracy holds, I'd drop them (fewer chunks, simpler ingest). I'd also try a header only for FTS, or with just the section name.

## 2026-10-07 Decimal-safe chunking stays on

**Context:** 4 % of chunks (148 of 3,768) had broken numbers, like "$10. 7 billion" or "$19.\n8 billion". I suspected the HTML reader first, but the stored document text was fine. The break came from `TextChunker.SplitPlainTextLines` inside `Chunker`, which splits long lines at periods, including one inside a number. The fix replaces a period between digits with a placeholder before splitting and puts it back afterwards. Step b4 on top of b3.

**Options:** leave it, or protect decimals during splitting.

**Decision:** protect them (`Chunking:ProtectDecimals = true`).

**Why:** on the Apple 10-K, broken numbers went from 8 of 296 chunks to 0. It's the only step in the ladder with a statistically significant retrieval gain: MRR 0.490 → 0.572, permutation test p = 0.0085 (retrieval) and p = 0.032 (full). hit@5 67 % → 75 %, accuracy 67 % → 70 % (2:1, p = 1.0). It costs nothing: no extra tokens, no latency, one regex.

**Would revisit when:** I replace `TextChunker` with my own splitter, which wouldn't need this.

## 2026-10-07 Temperature is not a lever here

**Context:** `gpt-4o` accepts `temperature`, and the plan had experiment E5 to see how it affects accuracy and stability. Final config (vector + headers + decimals, 10 sources), 30 questions, each asked 3 times (90 answers per value).

**Options:** temperature 0, 0.2 and 0.6.

**Decision:** keep 0.2, the value every other measurement in the stage used. It's the default in `appsettings.json`.

**Why:** accuracy was 87 % at all three values. Not one question changed its verdict across three repeats at 0 or at 0.6, and false answers were 0 % everywhere. The answers are short and tied to a number from a source, so sampling has little room to vary. Cost didn't change either (170K input tokens per 90 answers), and p50 latency was 976, 1,065 and 1,127 ms, which is noise.

**Would revisit when:** questions need long explanations, or I switch to a reasoning model, where I'd vary `ReasoningEffort` instead.

## 2026-10-07 Prompt and exact search left as they are

**Context:** the plan included E6 (rewording the prompt and the refusal) and E7 (an HNSW index instead of exact search).

**Options:** run both experiments, or close them based on the runs I already had.

**Decision:** close them without running.

**Why:** E6: 0 % false answers in all 15 full runs, including 4 questions with no answer in the documents and the cross-tenant traps. Every refusal I looked at happened because the fact wasn't in the context, not because the prompt was too strict, so there was nothing to fix. E7: exact search over ~5,000 chunks in two tenants takes ~300 ms (retrieval p50 270–390 ms). HNSW would save milliseconds at the cost of approximate results and extra tuning for the tenant filter (`hnsw.ef_search`, iterative scans).

**Would revisit when:** E6 if false answers appear or the model changes; E7 when the corpus grows to tens of thousands of chunks per tenant and search p50 goes past ~1 s.

## 2026-10-11 Stateless MCP transport

**Context:** Stage 5 exposes search over MCP (`ModelContextProtocol.AspNetCore` 2.2.0, Streamable HTTP). The tenant comes from the JWT: `TenantResolutionMiddleware` writes `tenant_id` into a scoped `CurrentTenant`, which both the EF filter and the raw-SQL search read. The SDK supports stateless and stateful sessions.

**Options:** stateful (server-to-client requests, resumable streams), or stateless, where every POST stands alone.

**Decision:** stateless, set explicitly (`SessionMode = Stateless`) with a comment saying isolation depends on it.

**Why:** from the SDK source, only in stateless mode does a tool run inside the HTTP request's DI scope. In stateful mode each tool call gets a new scope from the root provider. I checked it with the isolation test: in stateful mode the tool saw an empty tenant, and the positive control (tenant B finds its own document) failed with an empty result. A test that only checked "never returns tenant A's chunk" still passed. So it fails closed rather than leaking, but quietly, and a test without a positive control wouldn't notice. All MCP isolation tests now check both directions. Stateless also means no sticky sessions behind a load balancer.

**Would revisit when:** I need server-to-client requests (sampling, elicitation). Then the tool would have to resolve the tenant from the request's claims itself.

## 2026-10-11 MCP exposes search only

**Context:** an MCP client is itself a strong model, so the tools could be plain retrieval (`search_filings`, `list_filings`, and the client writes the answer) or could also wrap `/api/ask`. I added a harness mode that answers each eval question through `claude -p` with the MCP tools (`claude-sonnet-5`) and compared it with the Stage 4 final `/api/ask` run, same 30 questions and corpus.

**Options:** search and list only; `ask` only; both.

**Decision:** search and list only.

**Why:** the agent with search matched the server pipeline: 87 % vs 87 %, 3:3, McNemar p = 1.0. When its first query misses, it rephrases and searches again (1.2 tool calls per question). Once it found the income-statement table that wasn't in the first query's top 20. Wrapping `/api/ask` would add no accuracy and put one model inside another. The costs: ~9× more expensive ($1.37 per 30-question run vs about $0.15 of gpt-4o tokens), ~8× slower (p50 8.1 s vs 0.95 s), and weaker refusals: 2 of 6 should-refuse questions got "not disclosed, but here are related figures" instead of the refusal sentence (0 of 6 for `/api/ask`). Neither was made up. I also noticed the agent read the title "Morgan 10-K" as Morgan Stanley and refused two JPMorgan questions without searching. For an agent, document titles are part of the interface.

**Would revisit when:** clients need a guaranteed refusal or cheaper answers (then I'd add `ask_filings` with a description of when to use it), or the client model is weak enough that my pipeline beats it.

## 2026-10-11 Neighbour-chunk tool stays off

**Context:** split tables lose their header row and caption. I built a tool that returns a chunk's neighbours by `ChunkIndex`, behind `Mcp:ContextTool`, so the agent could fetch them. Measured as M2: the previous entry's agent run with the tool on.

**Options:** expose it or keep it off.

**Decision:** off by default (`Mcp:ContextTool = false`). The code stays.

**Why:** the agent never called it: 0 calls in 30 questions, accuracy 87 % again, 0:0 against the run without it. With topK = 10 and a section header on every chunk, search results were enough. A tool the model ignores just makes the tool list longer.

**Would revisit when:** the eval set gets questions that need rows from both halves of a split table, or the default `topK` goes down.

## 2026-10-11 MCP auth uses the existing JWT, not OAuth

**Context:** the MCP endpoint needed auth that carries `tenant_id`. The MCP spec's answer for protected HTTP servers is OAuth 2.1 with Protected Resource Metadata. Lumen has a self-issued 60-minute JWT and no authorization server.

**Options:** the same JWT, sent as `Authorization: Bearer` and refreshed by Claude Code's `headersHelper`; long-lived personal access tokens; OAuth 2.1 (OpenIddict or Entra ID) with PRM.

**Decision:** the existing JWT with a token helper. No `/.well-known/oauth-protected-resource`, because without an authorization server it can't be valid (`authorization_servers` must not be empty).

**Why:** no new auth code, and it works: I made the helper return an invalid token first, and Claude Code got a 401, re-ran the helper and retried successfully with no action from me. `tools/mcp/lumen-token.sh` logs in and prints the header. When the header is missing, Claude Code and MCP Inspector both fall back to OAuth discovery and Dynamic Client Registration and get 404s. So the clients are ready for OAuth, and Claude Desktop and claude.ai, which require it, stay unsupported. PATs would add a token table and a second cross-tenant lookup (`IgnoreQueryFilters`) for a convenience the demo doesn't need.

**Would revisit when:** real users need to connect. Then OAuth 2.1 via Entra ID, which also opens up Claude Desktop, or PATs if users come before that.

## 2026-10-11 The agent eval runs in an isolated configuration

**Context:** agent mode runs `claude -p` for each question. The first M1 run scored 40 % accuracy and 100 % false answers, which made no sense next to `/api/ask`'s 87 % and 0 %.

**Options:** trust the run, or read the raw answers and find what was different.

**Decision:** read the answers. The agent now runs with my hooks disabled (`--settings '{"disableAllHooks":true}'`) and only project-level settings (`--setting-sources project`, from an empty temporary directory), so my own `~/.claude/CLAUDE.md` never reaches it. A refusal counts if the answer contains the refusal sentence.

**Why:** the answers were in Russian. My user-level CLAUDE.md overrode the "Always answer in English" rule from `--append-system-prompt`, so the number scorer couldn't read "$416 161 млн" or "46,9 %", and refusals came with an explanation that an exact match rejected. I tried `--safe-mode`, but it also disables the MCP server. After the fix the same config scored 87 % (40 % → 87 %, nothing else changed). Same lesson as the mixed-corpus baseline in Stage 4: when a metric jumps this much, check the environment first.

**Would revisit when:** Claude Code changes how it loads settings and memory, or I evaluate a different agent client. Each needs its own isolation check before I trust its numbers.

## 2026-10-12 Split tables keep their header, row group and caption

**Context:** a table longer than 250 tokens was split into chunks, and every continuation chunk lost its column header. The JPM 10-Q chunk with `CET1 capital | 14.2 | % | 15.1 | % …` had no years and no "Standardized" header, and its row-group label `Capital ratios - Standardized(a)` ended up in the previous chunk, so `/api/ask` refused. The caption ("The following table provides…") was in the preceding prose chunk, and the reader dropped empty cells, which shifted columns. Unstructured and Docling repeat headers only from `<thead>`/`<th>`, which EDGAR HTML never uses, so I had to detect the header from the cell grid. A prototype aligned the columns of 97.8–100 % of data tables in the four filings. I added 5 JPM questions whose answer row sits in a continuation chunk (41 questions, 35 scored).

**Options:** keep the current text; (D) a colspan-aware grid with `$`/`%` merged into numbers and empty cells kept; (D + A′ + B) the same, plus header rows and the open row-group label repeated on every continuation chunk and the caption copied onto every table chunk; (C) a key-value row format like `label | 2025: $34,550 | …`.

**Decision:** D + A′ + B on by default (`TableCells`, `TableHeaders`, `TableCaptions` = true). C stays off (next entry).

**Why:** accuracy 77 % → 80 %, MRR 0.524 → 0.565, hit@3 52 % → 62 %, hit@1 45 % → 48 %, 0 % false answers, for 3–9 % more chunks. It fixes the Stage 4 failure `g-cet1-q2-26` (refused → correct) and one of the new questions (CCB employees: not retrieved → rank 2, correct). It's not statistically significant: 2:1 against the baseline, McNemar p = 1.0, and n = 35 would need about 6:0. D alone went 0:2, both swaps between near-identical headerless tables, not lost rows. Downsides: a one-row continuation with a long repeated header became too similar to its neighbours and dropped out of the top 10, and three of the five new questions aren't retrieved at any setting. The measurement also caught a bug: a first chunk whose caption plus header went over budget was repacked without its caption (2 such chunks per JPM filing, 63 with format C). After the fix that count is 0 and the metrics didn't change.

**Would revisit when:** the eval set is big enough for significance, or I build a better header detector for text tables. Right now a table whose first numeric row comes late (the Rule 10b5-1 table: `… | 200,000`) gets a 4-row "header" that's too big to repeat, so `g-dimon-10b5-1` (holdout) still refuses. Tables whose header is in a separate `<table>` above them (143 in the JPM 10-K) aren't handled either.

## 2026-10-12 Key-value table format stays off

**Context:** once the header is known, every value can carry its column label (`Research and development | 2025: $34,550 | Change: 10% | …`, `Risk-based capital metrics › CET1 capital ratio | Standardized, JPMorgan Chase & Co.: 14.2% | …`). TableRAG and Docling's default serializer both work this way. Measured on top of the header and caption step.

**Options:** pipe rows with aligned columns, or key-value rows.

**Decision:** pipe rows (`TableFormat = Pipe`).

**Why:** no gain, big cost. Accuracy 80 % → 77 % (0:1, p = 1.0), MRR 0.565 → 0.561, while JPM 10-Q chunks grew 1,444 → 2,280 (+58 %) and the 10-K 2,596 → 3,166. It did move `g-cet1-q2-26` to rank 1, but that answer was already correct at rank 3. Some JPM header spans don't line up with their data columns, so a few labels get the wrong period.

**Would revisit when:** retrieval gets a reranker or a bigger context, so the extra chunks stop competing for 10 slots, or questions need a cell's column named inside the chunk itself.

## 2026-10-12 No context header in the ask prompt

**Context:** the context header ("Morgan 10-K (filed 2025-12-31) — section") goes into embeddings, FTS and MCP results, but `/api/ask` only sends the model the chunk text. I wanted to see if adding it as a `context="…"` attribute on each `<source>` helps. Turning it on changes `promptHash` from `ae0bb62cae85`.

**Options:** chunk text only, or text plus the context attribute.

**Decision:** text only (`Chat:SourceContextHeader = false`).

**Why:** no effect: 0:0 on the header/caption corpus, 80 % both ways, at about +250 input tokens per question. The table captions already put the needed words into the chunk text.

**Would revisit when:** the prompt or the model changes, or questions depend on which filing or section a chunk came from (like 10-K vs 10-Q comparisons, which still fail).

## 2026-10-12 Chiseled-extra base image, non-root

**Context:** in Stage 6A I packaged Lumen as a container so `docker compose up` brings up the API and Postgres without the .NET SDK. The base image decides the size, the attack surface, whether the app runs as root, and whether ICU is there.

**Options:** `aspnet:10.0` (full Ubuntu, a shell, root by default); `aspnet:10.0-noble-chiseled` (distroless, non-root, forced invariant globalization); `-chiseled-extra` (distroless, non-root, with ICU and tzdata).

**Decision:** `aspnet:10.0-noble-chiseled-extra`, built with `--platform=$BUILDPLATFORM` and `-a $TARGETARCH`, so amd64 and arm64 both build without emulation.

**Why:** the image is 202 MB on chiseled-extra vs 294 MB on the full one (−31 %), and it runs as UID 1654 with no shell or package manager. Plain chiseled would save about 15 MB more (52.8 vs 67.7 MB compressed per dotnet-docker's size report), but forces invariant globalization. My code is culture-invariant, but I haven't checked what that does inside Semantic Kernel's `TextChunker`, AngleSharp and PdfPig, and a silent change in chunk counts would invalidate every eval baseline. With no shell, compose can't run an HTTP healthcheck in the API container (no curl). Only Postgres has one, and I check the API from the host (`/health/ready` and `tools/compose-smoke.sh`).

**Would revisit when:** I run the Apple 10-K characterization test once in invariant mode and the chunk count holds; then I'd switch to plain chiseled. Or when a second replica needs a container healthcheck; then I'd add a `healthcheck` subcommand to the app.

## 2026-10-12 Migrate and seed on startup, behind flags

**Context:** in Production the app started with no schema, no users and no data, because migrations and seeding only ran in Development. I wanted the demo to come up with both tenants and all four filings already ingested.

**Options:** an EF migration bundle as a one-shot compose service before the API (what Microsoft recommends for automated deploys), or `MigrateAsync` plus seeding at startup behind `Database:MigrateOnStartup`, `Database:SeedDemoData` and `Demo:FilingsPath` (on in Development and compose, off by default).

**Decision:** startup with flags. Filings go through the normal `UploadDocumentCommand`, so a seeded document is the same as an uploaded one (chunks, embeddings, ingest profile, raw file). All tenants in the manifest are resolved before the first upload, and titles that already exist are skipped.

**Why:** one container and no extra build stage. A cold start with migrations and 4,650 chunks seeded on Fake embeddings takes 7.5 s to `/health/ready` (the Morgan 10-K alone, 2,596 chunks, takes 3.1 s). A warm restart takes 1.2 s since everything is skipped. It's safe with more than one instance: with EF Core 9+ on Npgsql, `MigrateAsync` takes `LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE` for the transaction (I checked the provider source), so two replicas run one after the other. Seeding has no such lock.

**Would revisit when:** I deploy to Azure App Service for real. There a migration bundle as a deploy step gives reviewable SQL and lets the app's database user drop DDL rights. Also if seeding with Azure embeddings pushes startup past the platform's timeout, or more than one replica seeds at the same time.
