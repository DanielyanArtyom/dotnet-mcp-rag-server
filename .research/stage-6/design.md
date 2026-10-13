# Lumen — Stage 6B design spec: split tables keep their header, caption and columns

Status: **approved by the user on 2026-10-11.** **Amended by `plan.md` → "Pre-implementation corrections"** (prototype on the four sample filings): `rowspan` is supported, footnote cells are dropped, year rules decide what counts as a data row, the header block allows up to 6 rows and excludes trailing group labels, the caption is the matching sentence cut to 40 words (the Dimon caption paragraph is 167 words), `TableHeaders` requires `TableCells`, unaligned tables render as compact pipe, and a dangling group label moves to the next chunk.

Inputs:
- the kickoff (`../discussion/stage-6-kickoff.md`, row 3: the tables work is part of Stage 6; deploying to Azure is skipped);
- the research note (`table-splitting-headers-and-captions.md`; cited below as **R§n**);
- the user's decisions from 2026-10-11: build R + D + A′ + B, measure C separately in this stage, measure the context header in the prompt as its own step, extend the eval set, and have Claude run the eval measurements.

Once approved, this spec feeds `writing-plans` → `plan.md`. Stage 6A ("Run anywhere": Dockerfile, compose, `/health`, config through env) gets its own design later.

## Why: what the research found

The kickoff named `a-gm-pct-fy25`, `a-china-fy25` and `a-rnd-fy25` as split-table failures. That came from Stage 4a observations on an older config. In the Stage 4 final corpus those three tables are **whole chunks**, and all three questions are **answered correctly** (`runs/20261007-094501_final-full`, first hit at rank 3/4/4; R§1.5). The table failures that actually exist are:

1. **Continuation chunks without the column header or row-group label.** Example: `g-cet1-q2-26`, refused. Chunk 48 of the JPM 10-Q is `CET1 capital | 14.2 | % | 15.1 | % | …`. The years and Standardized/Advanced headers are in an earlier chunk, and `Capital ratios - Standardized(a)` sits at the end of chunk 47. The holdout question `g-tbvps-delta` and the anchors of `g-cet1-2025`, `g-eps-2025` and `g-net-income-2025` are continuations too. A heuristic count puts about 30 % of Apple table chunks and 28–48 % of JPM table chunks as continuations (R§1.5).
2. **The caption lives in the preceding prose chunk.** Example: `g-dimon-10b5-1` (holdout), refused. The table is retrieved at rank 2, but the words "Rule 10b5-1 trading arrangements" are not in it. The chunker flushes prose before every table (`Chunker.cs:70-72`), so the caption paragraph always ends the previous chunk.
3. **Columns shift.** `HtmlDocumentReader.ExtractTableText` drops empty cells and ignores `colspan` (`HtmlDocumentReader.cs:135-149`), and 63 % of SEC table cells are empty (R§1.2). For example, the second `8` in `Percentage of total net sales | 8 | % | 8 | % | 8 | %` is 2024, not "Change". `$` and `%` also land in cells of their own.
4. **The context header never reaches the answering model.** It goes into the embedding input (`EmbeddingInput.cs:6`), the FTS vector and MCP results, but `RagPrompt.BuildUserMessage` emits only `source.Text` (`RagPrompt.cs:25-30`).

Prior art (R§2) does not transfer as-is. Unstructured and Docling repeat headers only when a table has `<thead>`/`<th>`, and EDGAR HTML has neither, nor `<caption>`. The working analogues are Microsoft's `sample-app-aoai-chatGPT` (caption and header in every table piece) and edgartools (SEC header heuristics, `$`/`%` merging). No source measures header repetition or caption prepending on its own (R§3), so the eval harness decides.

## Scope and success criterion

| Step | Change | Switch | Needs re-upload | Measured |
|---|---|---|---|---|
| R | Structured table on `DocumentElement` (reader keeps the grid) | none (always on, invisible) | no | no, plumbing |
| — | Extend the eval set with 3–5 JPM continuation questions | — | no | yes, as the new baseline t0 |
| D | Cell normalization: colspan grid, `$`/`%`/`)` merge, positional empty slots | `Chunking:TableCells` | yes | t1 |
| A′ | Continuation chunks repeat the header rows + last row-group label | `Chunking:TableHeaders` | yes | t2 (with B) |
| B | Caption copied into every chunk of its table | `Chunking:TableCaptions` | yes | t2 (with A′) |
| C | Key-value serialization `label \| column: value` | `Chunking:TableFormat` | yes | t3 |
| P | Context header shown to the `/api/ask` model | `Chat:SourceContextHeader` | no | t4 |

Header-row detection is shared plumbing for A′ and C. It has no switch of its own.

**Implementation delivers everything at once**, with every switch defaulting to off, the same way as Stage 4b. Measurements then walk the ladder by changing config and re-uploading where needed, never by reverting code. **Option E** (header inheritance from a sibling table, R§5) is out of scope: its only target, `a-gm-pct-fy25`, is already answered correctly.

**Success:**
- A step becomes a default in `appsettings.json` and gets a `DECISIONS.md` entry with its number when `compare` shows a significant flip count (McNemar p < 0.05), or a clear gain on the continuation-anchored questions without losses elsewhere.
- **False answers stay 0 %.**
- **With every new switch off, chunk text is byte-identical to today's.**

## Design

### 1. R: the reader keeps the table grid

`DocumentElement` (`Lumen.Application/Ingestion`) gains an optional `Table` payload:

```csharp
public class DocumentElement
{
    public ElementType Type { get; set; }
    public string Text { get; set; } = default!;
    public TableGrid? Table { get; set; }
}
// TableGrid { List<TableRow> Rows }  TableRow { List<TableCell> Cells }  TableCell { string Text; int ColSpan }
```

- `HtmlDocumentReader` fills `Table` for every `ElementType.Table` element. Each `<tr>` that belongs to the table (the same `Closest("table") == table` rule as today) becomes a row. Each `td`/`th` becomes a cell: `CleanText(InlineText(cell))`, with `colspan` parsed (default 1, invalid → 1). **Empty cells and rows are kept** in the grid, because the spacer row defines the column count.
- **`Text` stays exactly what `ExtractTableText` produces today.** So `Document.Content` (`UploadDocumentCommand.cs:141`) and the off-profile chunk text don't change.
- `rowspan` is ignored, as it is today. It doesn't appear in the sample filings' financial tables, and handling it would need a 2-D occupancy grid. The plan checks the samples. If `rowspan` shows up in data tables, the formatter falls back to the pipe text for that table.
- `PdfDocumentReader` leaves `Table` null. A table element without a grid always takes today's path, whatever the switches say.

The types are plain classes in `Lumen.Application/Ingestion`, next to `DocumentElement`. They are an ingestion-pipeline contract, not domain models, and they never reach the API.

### 2. `TableFormatter`: everything happens at chunk time

A new `Lumen.Infrastructure/Chunking/TableFormatter` (with small helper types as needed) turns a `TableGrid` plus the switches into the table's chunk texts. The `Chunker` calls it for a table element that has a grid **and** has at least one table switch on. Otherwise the chunker keeps today's `SplitTableIfOversized(Protect(element.Text))`.

Doing D at chunk time rather than in the reader (as R§8 proposed) keeps `Document.Content` and the reader untouched, and the ingest profile keeps describing chunking settings only.

#### 2.1 D: column layout and cell normalization (`TableCells`)

1. **Expand** each row into grid slots by `ColSpan`. A cell's text sits in its span. Every row of a well-formed SEC table sums to the same width (Apple: 18; R§1.2).
2. **Merge symbols row-wise** (edgartools rules, R§6):
   - a `$` cell attaches to the next non-empty cell in the row (`$` + `34,550` → `$34,550`);
   - a `%`, `)` or `)%` cell attaches to the previous non-empty cell (`10` + `%` → `10%`, `(4` + `)` → `(4)`).

   The merged cell takes over the union of the two spans.
3. **Logical columns.** Each grid column belongs to a logical column. A logical column is a maximal run of grid columns that some cell in the table occupies, and the spans of the widest-reaching non-empty cells define the boundaries. Grid columns empty in every row are dropped. A row's value for a logical column is the text of the cell occupying it, or `""`.
4. **Serialize (pipe):** cells joined with ` | `. Trailing empty slots are trimmed, inner empty slots are kept (`Percentage of total net sales | 8% | | 8% | | 8%`). Fully empty rows are skipped.
5. **Fallback:** if rows don't sum to one width, or logical columns can't be formed unambiguously, the table uses today's pipe text (`element.Text`). This is counted, not silent: the formatter returns a flag, and the chunker logs it at Debug with the table index. The test suite asserts the fallback rate on the four sample filings stays under a threshold that the plan fixes after measuring.

Column alignment on JPM tables whose header rows are narrower than their data rows is an open question (R§9). The plan resolves it against the samples: right-alignment of short header rows, or fallback. Unit tests pin whichever rule it picks.

#### 2.2 Header-row detection (shared by A′ and C)

On the normalized grid (D's steps 1–3, even when only A′/B is on; see §2.6):
- **Header block** = the leading non-empty rows before the first **data row**. A data row has a non-empty first (label) cell **and** at least one numeric cell. Numeric means it matches digits with optional `$ , . ( ) % -`, after the merge.
- **Guards:** a block longer than 4 rows, or longer than half the token budget, counts as "no header detected". A table whose first non-empty row is already a data row has no header.
- **Row-group label** = a body row with exactly one non-empty cell and no numeric cell (`Gross margin:`, `Capital ratios - Standardized(a)`).

This is simpler than edgartools' rule set (R§5) on purpose. Unit tests pin it on real Apple and JPM table fixtures, including a JPM title row (`Financial performance of JPMorganChase`) above `Year ended December 31, (in millions…)` and the year row. All three are header rows under this rule.

#### 2.3 A′: repeat the header in continuation chunks (`TableHeaders`)

When the table is split (its text exceeds `MaxTokensPerChunk`), every chunk after the first starts with:
- the header block;
- the last row-group label seen before the split point, unless the continuation's first row is itself a row-group label.

Rows are packed greedily as today, and the **prefix counts inside the budget**. If the header block plus one row would exceed the budget, that continuation goes without the prefix. No overlap is added.

#### 2.4 B: caption in every table chunk (`TableCaptions`)

- **Caption** = the element immediately before the table, if it is a `Paragraph` of at most 60 words that ends with `:` or contains (case-insensitive) `following table`, `as follows` or `summar` (R§4). A heading or a non-matching paragraph means no caption.
- Every chunk of the table starts with `Table: <caption>` (before A′'s header on continuations). The caption is **copied**: the prose chunk keeps it too, so prose-only answers don't lose it.
- The caption counts inside the budget. A caption over half the budget is cut at a word boundary to fit.
- JPM's in-table title row stays part of the header block (§2.2), so it repeats under A′.

#### 2.5 C: key-value serialization (`TableFormat = KeyValue`)

- Each data row becomes `<label> | <column header>: <value> | …`, for non-empty values only (`Research and development | 2025: $34,550 | Change: 10% | 2024: $31,370 | …`).
- A logical column's header is its covering header-block cells, top to bottom, joined with `, ` (`Standardized, JPMorgan Chase & Co.`). The title row is excluded: a header row with a single non-empty cell spanning the full width is a title, not a column header.
- Header rows are not emitted as rows of their own, and a row-group label is prefixed to the rows under it (`Capital ratios - Standardized(a) › CET1 capital ratio | …`).
- A table with **no detected header**, or whose columns can't be aligned, uses D's pipe text.
- A′'s header repetition is redundant under C, because every value carries its header, so C ignores `TableHeaders`. B still applies.
- **The validator rejects `TableFormat = KeyValue` without `TableCells = true`.**

#### 2.6 How the switches combine

| `TableCells` | `TableHeaders` / `TableCaptions` | `TableFormat` | Body text | Prefixes |
|---|---|---|---|---|
| off | off | Pipe | today's `element.Text` | none |
| off | any on | Pipe | today's `element.Text` | header detected on the normalized grid but emitted from today's line format (the matching leading lines); caption as §2.4 |
| on | any | Pipe | D's pipe | A′ / B as switched |
| on | any | KeyValue | C | B only |

A′ without D works on today's lines. The header block is then "the first *k* lines of today's text", where *k* = the header rows found on the normalized grid, which skips empty rows, the same rows today's text drops. That keeps the ladder honest: t2 is measured on D's text because D stays on (cumulative ladder), and the off-D combination exists only for completeness and tests.

The decimal guard (`Protect`/`Restore`) runs on the formatter's output exactly as it does on table text today.

### 3. Ingest profile and config

- `ChunkingOptions` gains `TableCells` (bool), `TableHeaders` (bool), `TableCaptions` (bool), `TableFormat` (`Pipe` | `KeyValue`, enum bound from string). All default off/`Pipe` in code and in `appsettings.json`.
- **Profile tokens are appended only when the switch is not at its default:** `;cells=v1`, `;thdr=v1`, `;tcap=v1`, `;tser=kv1`. With all of them off, the profile is still `chunk=250/60;hdr=v1;merge=0;dec=v1`, so the Stage 4 final corpus stays valid and t0 needs no re-upload. This deviates from the Stage 4b rule ("a switch that is off still appears"). String equality still identifies the configuration, because an absent token means off.
- `ChunkingOptionsValidator`: `KeyValue` requires `TableCells`.
- `/api/settings` shows the four new values. It already returns the current profile, so the profile string shows them too.
- **Integration test host:** pins all four off, as it does for `ContextHeaders`/`ProtectDecimals` (Fake embeddings match only byte-identical text).

### 4. P: context header in the `/api/ask` prompt (`Chat:SourceContextHeader`)

- When on, `RagPrompt.BuildUserMessage` adds `context="…"` to each `<source>` tag, with the chunk's `ContextHeader` attribute-escaped. The attribute is omitted when `ContextHeader` is null.
- `UserTemplateVersion` becomes `sources-xml-v2` when on, and stays `sources-xml-v1` when off. **`promptHash` becomes config-dependent:** the hash takes the effective template version. `/api/settings`, `/api/ask` and the harness's `run.json` keep reporting it, so runs with P on and off are distinguishable and the harness's prompt-hash comparison keeps working.
- `ChunkSearchResult` already carries `ContextHeader` (Stage 5, Task 1), so no search SQL changes.
- The system prompt text doesn't change. Whether the model needs a rule about the attribute is left to measurement (not in scope).
- Default off. The integration host doesn't need pinning, because Fake chat ignores the prompt, but tests cover both shapes.

### 5. Eval set extension

- Before any measurement, Claude writes **3–5 JPM questions whose answer row sits in the middle of a long table** (10-K and 10-Q; for example capital ratios, an income statement line, per-share data). They are written from the source HTML in `samples/edgar/`, before looking at system answers, as Stage 4 did.
- The anchor label for each is checked with the existing anchor lint against the current corpus. **The user approves them before t0.**
- Holdout questions are untouched. The holdout is run once, at the end, on the final configuration.
- The extended set gets a new eval-set hash. t0 is the new baseline for every later step.

### 6. Measurement ladder (Claude runs it; the ladder is cumulative)

Server: Azure (`gpt-4o` answers, `text-embedding-3-small`, `gpt-5-mini` judge), Stage 4 final config otherwise. Every step runs `retrieval` and `full`, then `compare` against the previous step.

| Run | Config | Re-upload | Compare with |
|---|---|---|---|
| t0 | Stage 4 final, extended eval set | no | — |
| t1 | + `TableCells` | yes | t0 |
| t2 | + `TableHeaders` + `TableCaptions` | yes | t1 |
| t3 | + `TableFormat=KeyValue` | yes | t2 |
| t4 | best corpus of t0…t3 + `SourceContextHeader` | if the best is not t3 | that corpus's run |
| holdout | final configuration | — | Stage 4 holdout (4/6) |

What to read beyond accuracy and flips:
- **hit@1**: repeated identical text across sibling chunks lowered it at Stage 4 b2;
- **the rank of the continuation-anchored questions** (R§1.5 plus the new ones);
- **chunk count and table-token share per filing**;
- **the fallback count** (§2.1);
- **unlabelable questions** (C changes table text, so anchors may stop matching).

If n is too small for significance, the per-question ranks go into the hands-on log, and the decision says so honestly.

Results go to `../discussion/stage-6-hands-on-findings.md`. Winning switches become `appsettings.json` defaults. `DECISIONS.md` gets an entry only for a decision backed by a number. The integration host keeps the switches off regardless.

## Testing

- **Reader (unit):** a table fixture yields a `TableGrid` with spans and empty cells preserved, and `Text` is unchanged against the current expected strings. A PDF table has `Table == null`.
- **`TableFormatter` (unit, real fixtures cut from the Apple 10-K and JPM 10-K/10-Q samples):**
  - grid expansion;
  - `$`/`%`/`)` merge;
  - logical columns and empty slots (Apple operating-expense table: the Change slots keep their position);
  - fallback on ragged rows;
  - header detection (Apple year row; JPM title + period + year rows; JPM `Name | Title | …`; a table without a header);
  - row-group labels;
  - A′ prefix on continuations and the budget guard;
  - B caption rule (`:` / `following table` / heading → none / too long → cut);
  - C output, including a 2-level JPM header and the no-header fallback.
- **Chunker (unit):**
  - with all table switches off, the chunk output for the existing chunker fixtures and for a grid-carrying table is **byte-identical** to today's;
  - with switches on, `TokenCount ≤ MaxTokensPerChunk` holds for every chunk;
  - the caption is still present in the prose chunk (copy, not move);
  - decimal protection still applies.
- **Profile/validator (unit):** the profile string for each switch combination, the unchanged string when all are off, and `KeyValue` without `TableCells` → validation error.
- **Prompt (unit):** `context` attribute present/absent and escaped; template version and hash differ between on and off; the off output is identical to today's.
- **Settings (integration):** `/api/settings` shows the new values. Existing tenant-isolation tests are unchanged and stay green, since no new SQL path is added.
- Build has 0 warnings, the full test suite is green, and `IgnoreQueryFilters` stays only in `UserLookupService`.

## Out of scope

- E (header inheritance across sibling tables).
- `rowspan`.
- PDF table grids.
- LLM-written table summaries (LlamaIndex style).
- Reindexing from stored raw files.
- Changing the system prompt wording.
- Stage 6A (containers, `/health`, env config).
- The two-replica stateless check (deferred).
