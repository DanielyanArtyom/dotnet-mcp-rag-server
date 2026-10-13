# Lumen Stage 2 — progress record

Status: **complete** (2026-09-18). All 14 tasks of `plan.md` implemented via `superpowers:subagent-driven-development` on branch `step-2` (nothing committed — the user commits by hand; task boundaries were tracked as `git write-tree` snapshots). Full per-task ledger with every reviewer finding, ruling and deferred minor: `.research/stage-2/sdd-ledger.md`.

## What was built
Upload endpoint -> extension-based reader factory -> `HtmlDocumentReader` (AngleSharp) / `PdfDocumentReader` (PdfPig) -> `List<DocumentElement>` -> table-atomic `Chunker` -> `DocumentChunk` rows; raw files on local disk; old plain-text create endpoint removed. See `design.md` (with its post-implementation amendment).

## Evidence
83/83 tests (59 unit + 24 integration), build 0 warnings. Real Apple 10-K FY2025 (1.4 MB HTML): 296 chunks (209 prose / 87 table), tokens per chunk min 4 / median 201 / p95 236 / max 250, 0.75 s upload, 210,479 chars of normalized content (raw visible text ~254k; chunk-text/content ratio 1.18 because of overlap).

## Real defects the review loop caught (plan-mandated code that was wrong)
- `LocalFileStorage`: path traversal via `extension` and `relativePath` (tenant escape / arbitrary write) — hardened + tests.
- `HtmlDocumentReader`: silently dropped text outside p/div/h*, duplicated nested-table rows, emitted the hidden iXBRL header as a paragraph.
- `PdfDocumentReader`: merged all tables on a page and appended them after the prose (order lost).
- `Chunker`: oversized-table splitting could exceed the token budget; empty elements produced empty chunks; NU1903 (`Microsoft.Bcl.Memory` 9.0.4) arrived transitively via `Microsoft.SemanticKernel.Core`.
- API: no authorization tests on the Admin-only upload; missing `filingDate` silently bound to 0001-01-01; `[MaxLength]` on a command record is not model-bound when the controller builds the command by hand.

## Open items for Stage 3/4 and for the user's hands-on tuning
Header row is not repeated when an oversized table is split; empty table cells are dropped (columns can misalign); PDF heuristic uncalibrated on real PDFs; `CharStart`/`CharEnd` approximate; `DocumentDto` returns full `Content` in list/upload (2 MB per list of 10 filings); orphan raw file if `CompleteAsync` throws; run `dotnet list package --vulnerable --include-transitive` online (was unreachable in the sandbox); try JPMorgan 10-K/10-Q and the bank tables next.
