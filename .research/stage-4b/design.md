# Lumen — Stage 4b design spec: retrieval and ingest improvements, one measured step at a time

Status: **draft, design approved in conversation 2026-10-06, written spec awaiting user review.** Inputs: the E0 baseline and E1 failure analysis (`../discussion/stage-4-hands-on-findings.md`, `../checklists/stage-4a-user-checklist.md` part 4), the mixed-chunking incident (`../discussion/stage-4-incident-mixed-chunking.md`), the user's decisions (`../discussion/stage-4b-design-decisions.md`), and the hybrid-search research (`../stage-4/hybrid-search-postgres-fts-rrf.md`). Feeds `writing-plans` → `plan.md` once approved.

## Why — what E1 found

On the E0 baseline (gpt-4o, `text-embedding-3-small`, 250/60 chunks, vector-only, 30 questions: hit@5 71 %, MRR 0.59, accuracy 67 %, false answers 0 %, false refusals 38 %), every failure the user inspected on Acme was a **retrieval** miss — the model refused correctly because the needed fact was not in its five sources:

1. **A table holding the number loses to prose about the same metric.** `a-rnd-fy25`: the prose chunk "The growth in R&D expense … was primarily driven by headcount" ranks first; the table chunk "Research and development | $ | 34,550 | …" is not retrieved. Table chunks carry row labels and digits only — their caption ("Operating expenses for 2025, 2024 and 2023 were as follows") sits in the neighbouring prose chunk. Same for `a-china-fy25`, `a-gm-pct-fy25`.
2. **The 10-Q loses to the 10-K.** For `a-buyback-q3fy26`, `a-siri-ai`, `a-dividend-change` all five sources come from the 10-K.
3. **Tiny chunks occupy top-5 slots.** "Apple Inc. | 2025 Form 10-K | 56" (14 tokens, a page footer) took two slots for `a-siri-ai`. Chunks ≤ 20 tokens: 489 of 3 768 (395 of them table fragments such as the tail of a split table, the rest footers and page numbers).
4. **Decimals are split.** "$10. 7 billion", "$19.\n8 billion" in 148 of 3 768 chunks (4 %). **Root cause is the chunker, not the reader:** `Documents.Content` holds "$10.7 billion" intact; `TextChunker.SplitPlainTextLines` splits long lines on `.` including the one inside a number, and the paragraph packer rejoins the pieces with a space or newline. (Earlier notes blamed `HtmlDocumentReader`; corrected here.)

Plus the incident lesson: **`/api/settings` reports the current config, but stored chunks reflect the config at upload time**, and nothing tied the two together.

## Scope and success criterion

Six steps, each a separate measured change (user decision #7: one run per change):

| Step | Change | Needs re-upload | Measured |
|---|---|---|---|
| 0 | Ingest profile on `Document` + harness corpus check | yes (once, to stamp profiles) | no — safety net |
| — | Re-run baseline on eval-set `8a02fdeeef19` | no | yes (new reference) |
| 1 | Hybrid search: Postgres FTS + RRF (E2) | no (generated column) | yes |
| 2 | Contextual chunk header | yes | yes |
| 3 | Merge chunks ≤ N tokens into a neighbour | yes | yes |
| 4 | Keep decimals intact in the chunker | yes | yes |
| 5 | Per-document diversity cap in the result list | no | yes |

**Implementation delivers all six steps at once**, every switch defaulting to off (the plan's tasks follow the step order, but nothing waits for measurements). The user then walks the ladder by flipping config and re-uploading where marked. The ladder is **cumulative**: each step stays on for the next, every `compare` is against the previous rung. Each step ships behind a config switch whose default is "off", so the ladder is walked by changing config (and re-uploading where marked), never by reverting code.

Success: higher hit@5 / MRR and accuracy than the re-run baseline; a step goes into `DECISIONS.md` when `compare` shows a significant flip count (McNemar p < 0.05) or a clear hit@5/MRR gain, with the number. **False answers stay 0 %** and tenant isolation is covered by a test on every new SQL path.

Out of scope (later E-experiments if the post-4b breakdown points at them): BM25 extensions (`pg_textsearch`, ParadeDB), convex score combination instead of RRF, thousands-separator stripping in FTS, putting the section into the prompt, repeating a split table's header row, HNSW, `ContextTopK` tuning, temperature.

## Step 0 — ingest profile on the document

- `Document` gains `IngestProfile` (string, nullable for rows created before this step). It is computed at upload from the effective ingest settings and stored with the document.
- Format, stable and human-readable: `chunk=250/60;hdr=off;merge=0;dec=off`. Every ingest switch introduced in steps 2–4 adds its key; a switch that is off still appears, so two profiles are comparable by string equality.
- One Infrastructure service builds the string from `ChunkingOptions`; Application reads it through an interface (`IIngestProfileProvider.Current`) inside `UploadDocumentCommandHandler`.
- `GET /api/documents` (and `/{id}`) return `ingestProfile`; `GET /api/settings` returns the server's current `ingestProfile`.
- Harness: after login and before any question, for every tenant it compares each document's `ingestProfile` with the server's. Any mismatch — including a null profile — **aborts the run with exit code 1**, listing the documents and both profiles ("re-upload these documents"). The run header records the profile and per-document chunk counts.
- Migration adds the nullable column; no backfill (old rows correctly read as "unknown profile").

## Step 1 — hybrid search (E2)

Per the research file, SQL already validated read-only on the live database.

- **Column:** `DocumentChunks."SearchVector" tsvector GENERATED ALWAYS AS (to_tsvector('english', coalesce("ContextHeader", '') || ' ' || "Text")) STORED` + a GIN index. Declared in EF Core as a shadow property with `HasComputedColumnSql(..., stored: true)`; Domain and mapping are untouched. The same migration adds the nullable `ContextHeader` column used by step 2 (null until headers are switched on), so the generated column never needs a second migration.
- **Query text:** an **OR** tsquery built from the question's own lexemes (`tsvector_to_array(to_tsvector('english', @q))`, each `quote_literal`-ed, joined with `|`). Never `plainto_tsquery` / `websearch_to_tsquery`: they AND every word and match 0 chunks on "How much did Apple spend on research and development in fiscal 2025?". A question with no lexemes (all stop words) yields an empty lexical branch — the fusion degrades to vector-only, no error.
- **Ranking:** lexical branch `ts_rank_cd(SearchVector, q)` (normalization 0), vector branch cosine distance — exactly the current SQL.
- **Fusion:** Reciprocal Rank Fusion in one statement — two CTEs, **each with its own `WHERE c."TenantId" = @tenantId` and `EmbeddingModel` filter**, each limited to `@candidates`; `FULL OUTER JOIN` on chunk id; score `w_lex/(k + r_lex) + w_vec/(k + r_vec)` (missing rank contributes 0); join `Documents` with the tenant predicate as today; `ORDER BY score DESC, c."Id" LIMIT @topK`.
- **Distance in the result:** `ChunkSearchResult.Distance` keeps meaning cosine distance (computed for every returned row, including lexical-only hits), so `/api/search`, `/api/ask` sources and logs stay comparable across modes. A new `Score` (RRF score; null in vector mode) is added to `ChunkSearchResult`, `SearchResultDto` and `AskSourceDto`.
- **Config** (`Search` section, new `SearchOptions` in `Lumen.Infrastructure/DependencyInjection/`, validated on start): `Mode = Vector | Hybrid` (default `Vector`), `RrfK = 60`, `Candidates = 50` (≥ topK, ≤ 200), `LexicalWeight = 1.0`, `VectorWeight = 1.0`. `/api/settings`: `searchMode` becomes the real mode, plus a `search` object with these values.
- **One repository, two SQL texts.** `ChunkSearchRepository` picks the statement by mode; both keep the hand-written tenant predicates. `IChunkSearchRepository`'s signature gains the query text (the lexical branch needs it); `/api/search` and `/api/ask` both go through it — still one query path per mode, no `IgnoreQueryFilters`.
- **Isolation:** the existing "identical text in two tenants" integration test is extended to hybrid mode, and a new test seeds identical *lexical* content (a rare token present in both tenants) to prove the lexical branch alone never returns the other tenant's chunk. **Step 1 does not ship without these tests.**

## Step 2 — contextual chunk header

- **What:** every chunk gets a header `"{Document.Title} (filed {yyyy-MM-dd}) — {Section}"`, e.g. `Apple 10-k (filed 2025-09-27) — Operating Expenses`. Without a known section: `"{Title} (filed {date})"`.
- **Section detection** (in `Chunker`, which already walks `DocumentElement`s in order): the section is the most recent element that is either `ElementType.Heading` or a heading-like paragraph — ≤ 8 words, ≤ 80 characters, no terminal `.`/`:`/`;`, no `|`, contains a letter. SEC HTML rarely uses `<h1>`–`<h6>`; headings such as "Gross Margin", "Capital Return Program", "Item 7. Management's Discussion…" arrive as short paragraphs. Heading-like paragraphs still flow into prose chunks as today; detection only labels.
- **Where it lives:** a new column `DocumentChunks."ContextHeader"` (nullable text), **not** inside `Text`. The header is used for the embedding input (`header + "\n" + text`) and, via the generated column from step 1, for FTS. `Text`, `TokenCount`, the 250-token budget, eval anchors and the prompt are unchanged — `promptHash` stays the same, so the step measures the retrieval effect alone. Putting the section into the prompt is a later, separate experiment.
- `DocumentChunk` (Domain) gains `ContextHeader`; `GET /api/documents/{id}/chunks` returns it so the harness and the user can see it.
- Switch: `Chunking:ContextHeaders` (bool, default false) → profile key `hdr=v1|off`. Title and filing date reach the chunk through the upload handler (the chunker returns the section per chunk; the handler composes the header — the chunker stays free of document metadata).

## Step 3 — merge short chunks

- Switch: `Chunking:MergeBelowTokens` (int, default 0 = off; the ladder uses **20**) → profile key `merge=N`.
- After chunking a document, every chunk with `TokenCount ≤ N` is appended to the **previous** chunk if the combined token count fits the budget, otherwise prepended to the **next** chunk if that fits, otherwise kept as is. Joined with `"\n"`; the merged chunk keeps the larger neighbour's `ChunkType` and section; `ChunkIndex` is renumbered contiguously.
- N = 20 is chosen from the measured distribution: ≤ 20 tokens are footers, page numbers and one-row table fragments; 21–40 already contain meaningful rows ("Net excess eligible HQLA | $ | 138,052 …").
- The 250-token budget is never exceeded.

## Step 4 — keep decimals intact

- Switch: `Chunking:ProtectDecimals` (bool, default false) → profile key `dec=v1|off`.
- In `Chunker`, before `TextChunker.SplitPlainTextLines`, every `(\d)\.(\d)` gets its dot replaced by a placeholder character that is not a sentence delimiter (`U+2024 ONE DOT LEADER`); after splitting and packing, the placeholder is restored to `.`. Token counting happens on restored text so the budget stays exact.
- Unit tests: "$10.7 billion", "$19.8 billion", "4.00%", "$200.00 per share" survive inside long lines that do get split; ordinary sentence splitting is unchanged.

## Step 5 — per-document diversity cap

- Switch: `Search:MaxChunksPerDocument` (int?, default null = off; the ladder uses **3** with `ContextTopK = 5`).
- Applied in SQL after ranking, in both modes: `row_number() OVER (PARTITION BY c."DocumentId" ORDER BY <rank>) <= @maxPerDocument`, then `LIMIT @topK`. With the vector query this means ranking a wider candidate set first (`@candidates`), so the cap does not starve the list.
- Effect: at most 3 of 5 sources from one filing, so the quieter filing (usually the 10-Q) gets at least 2 slots when it has relevant chunks. Risk, to be read off the measurement: single-document questions may lose a useful 4th/5th chunk from their own filing.
- `/api/search` honours the same setting (the harness's retrieval mode must see what `/api/ask` sees).

## Harness changes

- Corpus check (step 0) before every run; profile and per-document chunk counts in `run.json` and the console header.
- `ApiContracts`: `DocumentInfo` gains `IngestProfile`; search hits and ask sources gain `Score`; settings are already stored raw.
- **Sources in `results.jsonl`**: each question row gains `sources: [{ rank, chunkId, documentId, chunkIndex, chunkType, distance, score, textPreview (first 200 chars) }]` so E1-style analysis is done from the run file instead of re-asking in Swagger (user agreed in the checklist discussion).

## Error handling

| Situation | Behaviour |
|---|---|
| Question has no lexemes in hybrid mode | lexical branch empty → vector ranks only; no error |
| Invalid `Search` options (e.g. `Candidates < topK`, unknown mode) | fail on start (`ValidateOnStart`), like `Chat` |
| Document profile ≠ server profile | harness exit 1 with the list; the API itself keeps working |
| Chunk cannot be merged without exceeding the budget | kept as is |

## Testing

- **Unit:** OR-tsquery construction is done in SQL — covered by integration tests; heading-like paragraph detection (positives and negatives, e.g. a short sentence ending with "."); header composition; merge rules (previous / next / keep, budget respected, renumbering); decimal protection; profile string for each switch combination; `SearchOptions` validation; harness profile check and `sources` serialization.
- **Integration (Testcontainers, Fake providers):** migration applies; hybrid returns a chunk that only matches lexically (Fake embeddings are meaningless for semantics, which makes this test sharp); **tenant isolation in hybrid mode and in the lexical branch alone**; diversity cap honoured in both modes; `/api/documents` and `/api/settings` expose the profile; the end-to-end harness test aborts on a profile mismatch.
- `IgnoreQueryFilters` still appears exactly once in `src/`.

## Documentation

- Correct the "reader splits decimals" statements in `../discussion/stage-4-incident-mixed-chunking.md`, `../discussion/stage-4-hands-on-findings.md`, `../discussion/stage-4b-design-decisions.md` (root cause: chunker).
- `CLAUDE.md`: Stage 4b status, `Search` section and switches, the re-upload rule tied to `IngestProfile`.
- `DECISIONS.md`: only after measured steps, one entry per decision with its number.
- A user checklist for walking the ladder (config + re-upload + two runs + `compare` per step).
