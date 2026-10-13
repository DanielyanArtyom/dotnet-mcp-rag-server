# Lumen — Stage 2 design spec: Ingest & chunking (SEC EDGAR filings)

Status: approved and implemented (2026-09-18) — see the amendment below for the points where the build differs from this text. Drafted via brainstorming + hands-on exploration, 2026-09-13/16.

> **Amendment (post-implementation, 2026-09-18):** left as-written otherwise. Differences found while building/reviewing: (1) no `DELETE /api/documents/{id}` existed in Stage 1, so the "kept as-is" list only has the two GETs (and the chunk FK cascade-delete is untested/unreachable for now); (2) `Document.RawFilePath`/`FilingDate` are required columns; (3) the upload request cap is 20 MB and `title`/`filingDate` are required form fields (`title` max 500) — invalid/corrupt/unsupported files return 400 before anything is written; (4) `LocalFileStorage` validates the extension (`^\.[A-Za-z0-9]{1,10}$`) and enforces a root-containment check on reads (path-traversal hardening — the plan's original code had a hole); (5) `HtmlDocumentReader` skips `display:none`/`ix:header` content, descends into block wrappers, drops empty table cells and de-duplicates nested tables; `PdfDocumentReader` emits one Paragraph per text line and tables in document order (uncalibrated heuristic: gaps > 1.5x word height, >=3 cells = table row); (6) `CharStart`/`CharEnd` are approximate running offsets over overlapping chunks; (7) options are bound in `Lumen.Api`'s `Configure()`, relative `DocumentStorage:RootPath` resolves against the content root; (8) the `Microsoft.Bcl.Memory` 10.0.12 pin exists to clear NU1903 from `Microsoft.SemanticKernel.Core`. (9) after the first hands-on review (2026-09-20): raw files are stored as `{tenant-name-slug}_{TenantId}/{title-slug}_{DocumentId}.{ext}` so a human can tell which file is which (names slugified to `[A-Za-z0-9-]`, max 50 chars, empty -> `unnamed`; the tenant name comes from an `ITenantLookupService`); `DELETE /api/documents/{id}` (Admin, 204/404) now exists — row first, cascade for chunks, raw file last; `DocumentDto.Content`/`ChunkCount` are nullable and omitted from JSON unless requested (`?includeContent=true` on `GET`, `ChunkCount` only in the upload response); table cells stay exactly as they are in the HTML (a first attempt to hard-code the rejoining of `$`/`%`/`)` cells was reverted — the user wants the parser to decide such cases generically, still open); Domain files live in `Entities/` and `Enums/` with namespaces `Lumen.Domain.Entities` / `Lumen.Domain.Enums`; cross-tenant access answers 404 (not 403) because the global query filter makes the row invisible and telling "exists but forbidden" would leak existence. Real-filing evidence: Apple 10-K FY2025 (1.4 MB HTML) -> 296 chunks (209 prose / 87 table), tokens per chunk min 4 / median 201 / p95 236 / max 250, 0.75 s upload, 210,479 characters of normalized content.

Rationale/evidence for the decisions below lives in `.research/stage-2/*.md` (four research files: SEC EDGAR filing formats, chunking strategy landscape, .NET parsing/chunking libraries, chunking testing/debugging) plus the user's own hands-on inspection of real Apple and JPMorgan filings (samples in `samples/edgar/`, gitignored, exploration-only).

## Scope

Stage 2 builds file-upload-based document ingestion and a chunking pipeline, extending Stage 1's minimal `Document` CRUD vertical slice. Explicitly **out of scope**: embeddings/vector storage (Stage 3), retrieval (Stage 4), MCP server (Stage 5), direct SEC EDGAR API integration (deliberately not built — see below), XBRL fact extraction (deferred — HTML/PDF text content only for now).

## Corpus

Two real SEC EDGAR filers, one per tenant, chosen deliberately for contrast: **Apple Inc.** (relatively clean HTML) and **JPMorgan Chase & Co.** (heavier, more table-dense financial-institution filings) — 10-K + 10-Q each. Exact seed-data wiring (repurposing the existing Acme/Globex tenants vs. new ones) is a `plan.md`-level detail, not decided here.

## Ingestion trigger: file upload, not direct EDGAR integration

Lumen does not talk to `data.sec.gov`/`www.sec.gov` itself. A filing is downloaded by hand and uploaded through the API. Two reasons: (1) this validates the upload path independent of data source — the actual goal is exercising the future MCP server, and upload is the most direct way to test that regardless of where a document came from; (2) it keeps the pipeline **format/domain-agnostic on purpose** — Lumen isn't "a SEC EDGAR tool," it's a general document-RAG system currently stress-tested with SEC filings because they're messy and hard to source cleanly elsewhere. A future tenant could just as easily upload something unrelated (a restaurant menu was the example raised) — nothing in the ingestion pipeline may assume SEC-specific structure.

## Multi-format support: `IDocumentReader` abstraction

Central seam: **`IDocumentReader`** (`Lumen.Application`) — takes a raw file (stream + filename) and produces a normalized `List<DocumentElement>`. **`DocumentElement`** (`Lumen.Application`) is `{ ElementType Type, string Text }`; `ElementType` is `Heading | Paragraph | Table` — an enum, deliberately extensible, same reasoning as `DocumentChunk.ChunkType` below. Everything downstream (the chunker) works only against this model and never knows what the original file format was.

Two concrete readers for Stage 2 (`Lumen.Infrastructure`):

- **`HtmlDocumentReader`** — parses via **AngleSharp**, chosen over `HtmlAgilityPack` because hand-inspection of real Apple/JPMorgan filings during design found messy *styling* (inline styles, empty spacer `div`/`td`, entity artifacts like `&nbsp;`/`&#8220;`), not actually broken tag structure — AngleSharp's spec-compliant DOM/`querySelector` API was judged more valuable here than HtmlAgilityPack's malformed-markup tolerance. Walks the DOM, classifies block-level elements into `Heading`/`Paragraph`/`Table`, strips the presentational noise observed firsthand in both companies' real filings.
- **`PdfDocumentReader`** — parses via **PdfPig**, with full table-structure reconstruction from word/glyph bounding-box positions (PdfPig has no built-in table detection). The exact clustering algorithm is **not** specified in this design — flagged as a dedicated research/spike task for `plan.md`, since no verified reference implementation exists in the .NET ecosystem for this specific problem.

**File-type detection at upload:** extension-based, via ASP.NET Core's built-in `FileExtensionContentTypeProvider` (same pattern as an existing sibling project's `MimeTypeHelper`) — not client-supplied `Content-Type`, not byte-sniffing. An `IDocumentReaderFactory` (`Lumen.Application` interface, `Lumen.Infrastructure` implementation) maps the resolved MIME type to the right reader; an unrecognized type throws the existing `ValidationException` → 400.

## Raw file storage

Uploaded files live on the local filesystem, not as a DB blob column: `{DocumentStorage:RootPath}/{TenantId}/{DocumentId}.{ext}`, root path configurable via `appsettings` (same pattern as `ConnectionStrings`/`Jwt`), default `App_Data/Documents` (relative to the `Lumen.Api` content root) — deliberately **not** `wwwroot` (raw filings must never be reachable via unauthenticated static-file serving). `Document.RawFilePath` stores the **relative** path only (`{TenantId}/{DocumentId}.{ext}`).

Tenant-scoped subfolders are defense-in-depth: since files sit outside EF Core's global query filter, isolation on disk depends entirely on application code deriving the path only from the authenticated caller's own `TenantId`/the target `Document.Id` — never from raw client input.

**Noted for later, not decided now:** local disk is fine at Stage 2/dev scale; Azure App Service's local filesystem isn't guaranteed durable across restarts/scale-out, so this may need to move to Azure Blob Storage at Stage 6. Candidate `DECISIONS.md` "when I'd reconsider" entry, once there's a real number to cite.

## Domain model changes

- **`Document`** (`Lumen.Domain`, extends Stage 1's minimal version) gains `RawFilePath` (string) and `FilingDate` (date). No form-type/company field added — company identity is already implied by `TenantId` (one company per tenant), and a form-type field would bake SEC-specific structure into a model meant to stay domain-agnostic.
- **`DocumentChunk`** (`Lumen.Domain`, new) — one universal type, not split into separate prose/table entities: `Id`, `DocumentId`, `TenantId` (tenant-owned, same `ITenantEntity`/global-query-filter pattern as `Document`), `ChunkIndex` (int), `Text` (string), `TokenCount` (int), `CharStart`/`CharEnd` (int, position in the normalized text), `ChunkType` (enum: `Prose | Table`, extensible for future content shapes). Rationale: YAGNI — nothing concrete yet needs table-specific fields, and the enum absorbs future content shapes without another EF entity/mapping/migration.
- `IRepository<DocumentChunk>` added to `IUnitOfWork`, mirroring the existing `IRepository<Document>` pattern exactly (shared `AppDbContext`, same tenant-stamping via `SaveChangesAsync`).

## Chunking

The chunker walks the `List<DocumentElement>` from the reader:

1. Accumulate consecutive `Heading`/`Paragraph` elements into a "prose run."
2. On hitting a `Table` element: close the current prose run (even if under budget), run it through `Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextParagraphs` (token-budgeted, overlapping paragraph splitting — reused rather than hand-rolled) to emit `Prose` chunks, then emit the table itself as one atomic `Table` chunk.
3. An oversized table (bigger than `MaxTokensPerChunk` alone) gets an internal split — exact strategy (e.g. row-group splitting, possibly duplicating the header row into each piece) deferred to `plan.md`, same reasoning as the PDF algorithm above: no verified reference implementation to point to yet.
4. Token counting via `Microsoft.ML.Tokenizers` (`TiktokenTokenizer`) — matches the tokenizer family of the already-settled Azure OpenAI embeddings backend, so `DocumentChunk.TokenCount` reflects what the Stage 3 embedding call actually sees.

**Defaults** (from hands-on ChunkViz testing against real Apple/JPMorgan excerpts during design — prose held together well at ~1000 characters/25% overlap): `MaxTokensPerChunk = 250`, `OverlapTokens = 60` (25%), as a `ChunkingOptions` class in `Lumen.Infrastructure/DependencyInjection/` (same convention as `JwtOptions`), bound from `appsettings`, not hardcoded — expected to be retuned empirically against the full real corpus, per this project's stated philosophy of finding chunking parameters empirically rather than on paper.

**Known risk, not blocking:** `TextChunker.SplitPlainTextParagraphs` has an unresolved-status upstream report of a stack overflow on very large inputs (research finding, status unverified) — addressed via the characterization test below, not by avoiding the library.

## API surface

- **Removed:** `POST /api/documents` (plain `Title`+`Content` JSON) and `CreateDocumentCommand` — superseded entirely by upload. Keeping both would leave two divergent ways to create a `Document`, one of which never produces chunks.
- **Kept as-is:** `GET /api/documents`, `GET /api/documents/{id}` (a `DELETE` endpoint never existed in Stage 1 — see the amendment at the top).
- **New: `POST /api/documents/upload`** (`multipart/form-data`, `Role.Admin` only — same authorization rule Stage 1 already established for ingestion). Runs the full pipeline synchronously within the request (store raw file → `IDocumentReader` → chunker → persist `Document` + `DocumentChunk[]`) and returns the created `Document` including a chunk count. No background job/queue infrastructure — observed file sizes (~1.5MB HTML) and expected processing cost don't currently justify it; revisit only if a real, measured latency problem shows up.
- **New: `GET /api/documents/{id}/chunks`** — lists a document's chunks with metadata (`ChunkIndex`, `Text`, `TokenCount`, `ChunkType`, `CharStart`/`CharEnd`). Doubles as the manual chunk-inspection tool identified in research — an authenticated endpoint instead of a separate dump script.

## Testing

- **Unit** (no DB, no file I/O): `HtmlDocumentReader`/`PdfDocumentReader` against small synthetic fixtures → correct `DocumentElement` typing; chunker given a `List<DocumentElement>` directly → correct token-budget/overlap behavior and, critically, that a `Table` element always becomes its own atomic chunk.
- **Integration** (Testcontainers Postgres + `WebApplicationFactory`, as in Stage 1): end-to-end upload → chunk persistence on a small fixture; **tenant isolation extended to chunks** (two tenants, `GET .../chunks` never crosses tenant boundaries) — same "required deliverable" status as Stage 1's flagship isolation test; a **characterization test against a real, committed Apple 10-K fixture** (copied into `tests/Lumen.IntegrationTests/Fixtures/`, not read from the gitignored `samples/edgar/`) asserting the pipeline completes without error and no chunk exceeds `MaxTokensPerChunk`.

## Deferred / open items

Not designed now, left for `plan.md` or later empirical tuning: exact PDF table-reconstruction clustering algorithm; exact oversized-table internal-split strategy; whether `MaxTokensPerChunk=250`/`OverlapTokens=60` survive contact with the full real corpus; whether local-disk raw-file storage needs to move to Azure Blob Storage (Stage 6 concern); exact seed-data wiring for the Apple/JPMorgan tenants.
