# Table splitting: header rows, captions and cell merging

Research note for the Stage 6 table work (kickoff: `../discussion/stage-6-kickoff.md`, row 3).
Written 2026-10-11. Sources are primary: library source code (pinned to the commit read), official
docs, arXiv papers. Lumen facts come from the code at `c009696` and from **read-only SELECTs against
the local `lumen` DB** (profile `chunk=250/60;hdr=v1;merge=0;dec=v1`, the Stage 4 final corpus).

---

## 0. TL;DR

- **The diagnosis needs a correction before anything gets built.** None of the four questions the
  kickoff names fails because a split table lost its header row. In the current corpus
  `a-gm-pct-fy25`, `a-china-fy25` and `a-rnd-fy25` sit in **whole, unsplit** table chunks, and all
  three are already **answered correctly** in the final config (`runs/20261007-094501_final-full`,
  first hit at rank 3/4/4). `g-dimon-10b5-1` also lands in a first chunk that has its header row.
  What it lacks is the **caption** ("Rule 10b5-1 trading arrangements"), which sits in prose.
  The questions that really suffer from **continuation chunks without headers** are on the JPM side:
  `g-cet1-q2-26` (refused in the final run), `g-tbvps-delta` (holdout, refused), and the anchor
  chunks of `g-cet1-2025`, `g-eps-2025` and `g-net-income-2025` (correct, but via other chunks).
- `a-gm-pct-fy25` is a **third failure shape** the kickoff doesn't list. Apple's gross-margin
  percentages are a *separate* `<table>` with no header row; the years live in the previous sibling
  table. Repeating headers inside one table can't fix that.
- Prior art: **Unstructured** (`repeat_table_headers`, default on), **Docling** (`repeat_table_header`,
  default on), **Microsoft.Extensions.DataIngestion** (row 0 repeated) and Microsoft's
  **sample-app-aoai-chatGPT** (caption + `<th>` row repeated in every mini-table) all repeat headers.
  But the first two detect headers **only from `<thead>`/`<th>`**, and SEC filings have zero `<th>`
  and zero `<thead>` (measured below). So on EDGAR HTML they would repeat nothing. Only
  **edgartools** has SEC-specific header heuristics (year rows, "Year ended", "(in millions)") and
  `$`/`%`/`)` cell merging.
- Evidence on serialization formats is **thin and contradictory for retrieval**. HTML did best for
  LLM *table understanding* (Sui et al., WSDM'24), Markdown did best in TQA-Bench, and for dense
  *retrieval* "no format uniformly preferable" (TEmBed-T, 2026). The one financial-RAG chunking paper
  (Jimeno-Yepes et al. 2024) keeps tables whole and prefixes a caption-derived description. It
  doesn't test header repetition.
- Recommendation (judgement): build a structured table model in the reader first (grid with
  colspan, `$`/`%` merge, header-row detection, caption = preceding paragraph). Then measure
  **A+B together** (repeat headers + caption in the chunk *text*) as one switch, and C (per-cell
  `label, column: value`) as a second switch.

---

## 1. Current state in Lumen

### 1.1 Data model

- `DocumentElement` is just `{ ElementType Type; string Text }`
  (`src/Lumen.Application/Ingestion/DocumentElement.cs:3-7`). `ElementType` is
  `Heading | Paragraph | Table` (`ElementType.cs:3-8`). **A table reaches the chunker as one flat
  string. Row/cell structure, colspans and header rows are gone by then.**
- `Document.Content` is `string.Join("\n\n", elements.Select(e => e.Text))`
  (`src/Lumen.Application/Documents/Commands/UploadDocumentCommand.cs:141`). Any reader-side change
  also changes the stored `Content`.

### 1.2 HTML table serialization (`src/Lumen.Infrastructure/DocumentReading/HtmlDocumentReader.cs`)

```csharp
// :135-149
foreach (var row in table.QuerySelectorAll("tr").Where(r => r.Closest("table") == table))
{
    var cells = row.Children
        .Where(c => TagOf(c) is "td" or "th")
        .Select(cell => CleanText(InlineText(cell)))
        .Where(text => text.Length > 0);          // <- empty cells dropped (problem 3)
    var line = string.Join(" | ", cells);
    if (line.Length > 0)
        lines.Add(line);                          // <- empty rows dropped
}
return string.Join("\n", lines);
```

- One line per `<tr>`, cells joined with `" | "`, empty cells and rows dropped, `colspan` ignored.
- Each `<table>` becomes its own `ElementType.Table` element (`:70-75`). Preceding `<p>`/`<div>`
  text becomes `Paragraph` elements, so the caption is a separate element (problem 2).
- The "`Gross margin percentage: Products | 36.8 | %`" format quoted in the findings is the chunk
  text with `\n` shown as a space. `Gross margin percentage:` is the table's own first row (a
  section label in a `colspan=3` cell). `36.8` and `%` are two `<td>`s.

What the raw grid looks like (Apple 10-K FY2025, `samples/edgar/aapl-20250927.html`,
parsed with lxml; `(text, colspan)`):

```
row0  ('',None) x18                                   <- width-defining spacer row, dropped
row1  ('',3) ('2025',3) ('',3) ('2024',3) ('',3) ('2023',3)              -> "2025 | 2024 | 2023"
row3  ('Products',3) ('$',1) ('112,887',1) ('',1) ('',3) ('$',1) ...     -> "Products | $ | 112,887 | $ | ..."
row4  ('Services',3) ('82,314',2) ('',1) ('',3) ('71,050',2) ...         -> "Services | 82,314 | 71,050 | ..."
```

Every row sums to the same 18 grid columns, so **`2025` (cols 3-5) sits exactly above `$` +
`112,887`**. Column alignment can be recovered from colspans. The reader just doesn't do it.

The Operating Expenses table shows the column shift (problem 3):

```
2025 | Change | 2024 | Change | 2023
Research and development | $ | 34,550 | 10 | % | $ | 31,370 | 5 | % | $ | 29,915
Percentage of total net sales | 8 | % | 8 | % | 8 | %       <- Change cells empty: 2nd "8" is 2024, not "Change"
```

JPM is worse. `Return on common equity | 17 | % | 18 | %` is followed by
`Return on tangible common equity | 20 | 22`, because only the first row of a block carries `%`.
And `CET1 capital ratio | 14.2 | % | 15.0 | % | 14.2 | % | 16.1 | %` sits under a 2-level header
(Standardized/Advanced × JPMorgan Chase & Co./Bank, N.A.) that can only be resolved through the grid.

Measured over the sample filings (lxml; all `<table>`s, incl. layout tables):

| filing | non-empty tables | est. > 250 tok (chars/4) | `<th>` | `<thead>` | `<caption>` | cells | empty cells | `$` cells | `%` cells |
|---|---|---|---|---|---|---|---|---|---|
| aapl-20250927 | 54 | 12 | 0 | 0 | 0 | 6,443 | 4,034 (63 %) | 277 | 57 |
| aapl-20260627 | 32 | 9 | 0 | 0 | 0 | 4,762 | 3,044 | 239 | 39 |
| jpm-20251231 | 678 | 103 | 0 | 0 | 0 | 60,348 | 37,822 (63 %) | 2,922 | 512 |
| jpm-20260630 | 275 | 90 | 0 | 0 | 0 | 56,877 | 35,000 | 2,946 | 579 |

### 1.3 PDF tables

`PdfDocumentReader` builds the same `cell | cell` lines from word-gap heuristics
(`PdfDocumentReader.cs:21-38`, `FlushTable` `:44-51`). A line with fewer than `MinTableColumns = 3`
cells (`:9`) ends the table, so one-cell header rows like "Year ended December 31," become prose.
None of the corpus filings is a PDF. This note treats PDF as out of scope, except that any new
switch must not break it.

### 1.4 Chunker (`src/Lumen.Infrastructure/Chunking/Chunker.cs`)

- A table element flushes the pending prose run first (`:70-72`). **So the caption paragraph always
  ends up as the tail of the previous prose chunk.** Example: Apple chunk 170 (34 tok) =
  `Gross Margin\nProducts and Services gross margin ... were as follows (dollars in millions):`,
  chunk 171 = the table.
- `SplitTableIfOversized` (`:251-274`): if the whole table fits in 250 tokens it's one chunk.
  Otherwise rows are packed greedily into groups ≤ 250 tokens. **No overlap and no header carry-over.**
  A row over budget is cut by `TextChunker.SplitPlainTextLines` (`:276-282`).
- Table text also goes through `Protect` (decimal guard, `:76`, `:284`).
- Context header (`ContextHeaders=true`): `"{title} (filed yyyy-MM-dd) — {section}"`
  (`ContextHeaderBuilder.cs:7-11`), stored in `DocumentChunk.ContextHeader` (`Chunker.cs:51`).
  `section` updates only on `Heading` elements or heading-like paragraphs (`Chunker.cs:84-88`). A
  heading-like paragraph is ≤ 8 words and ≤ 80 chars, has no `|`, and doesn't end in `. : ; ,`
  (`SectionHeading.cs:10-24`). **A caption ending in `:` never becomes the section.** That's correct
  for a heading detector, but it means the caption is lost to the table.
- Where the context header goes:
  - Embedding input: `ContextHeader + "\n" + Text` (`src/Lumen.Application/Ingestion/EmbeddingInput.cs:5-6`).
  - FTS vector: `coalesce("ContextHeader",'') || ' ' || "Text"` (`DocumentChunkEntityConfiguration.cs:21`).
  - MCP result heading: `McpResultFormatter.cs:25,30`.
  - **Not** the `/api/ask` prompt. `RagPrompt.BuildUserMessage` emits only `title`, `filed`, `kind`
    and `source.Text` (`src/Lumen.Application/Ask/RagPrompt.cs:25-30`). Anything put only in
    `ContextHeader` helps retrieval but is invisible to the answering model. That matters for
    `g-dimon-10b5-1` (§1.5).
- `TokenCount` counts `Text` only (`Chunker.cs:47`). `ContextHeader` sits outside the 250 budget.
- Ingest profile: `chunk={max}/{overlap};hdr={v1|off};merge={n};dec={v1|off}`
  (`IngestProfileProvider.cs:8`). Options: `ChunkingOptions.cs:3-11`.

### 1.5 Where the named questions actually land (DB, current corpus)

Continuation = a table chunk right after a table chunk of ≥ 200 tokens with the same context header.
This is a heuristic, not ground truth.

| question | answer chunk | split continuation? | year/column header in chunk? | caption in chunk? | final run (`094501`/`094815`) |
|---|---|---|---|---|---|
| `a-gm-pct-fy25` | Apple 10-K #172 (74 tok) | **no**: whole `<table>` | **no**. The table has none; years are in sibling table #171 | no (#170) | correct, rank 3 |
| `a-china-fy25` | Apple 10-K #156 (197 tok) | no | yes (`2025 \| Change \| 2024 ...`) | no ("net sales by reportable segment" in prose) | correct, rank 4 |
| `a-rnd-fy25` | Apple 10-K #176 (183 tok) | no | yes | no ("Operating expenses ... were as follows") | correct, rank 4 |
| `g-dimon-10b5-1` (holdout) | JPM 10-K #256 (237 tok) | no (first chunk) | yes (`Name \| Title \| ... Aggregate number of shares to be sold(d)`) | **no**. "Rule 10b5-1 trading arrangements" is in prose | **refused**, rank 2 |
| `g-cet1-q2-26` | JPM 10-Q #48 | **yes** | **no**: `CET1 capital \| 14.2 \| % \| 15.1 \| % \| 14.2 \| % \| 15.1 \| %`, with the row-group label `Capital ratios - Standardized(a)` left at the end of #47 | no | **refused**, rank 4 (#1127, 2-level header) |
| `g-tbvps-delta` (holdout) | JPM 10-Q #27, 10-K #340 | **yes** (both) | no | no | refused (comparison, 10-Q chunk not retrieved) |
| `g-cet1-2025` / `g-eps-2025` / `g-net-income-2025` | 10-K #340 / #1205 / #422,#1205,… | yes (some anchors) | no | no | correct (rank 1/3/1 via other chunks) |

Scale of the continuation problem (same heuristic):

| doc | table chunks | likely continuations | table tokens / all tokens |
|---|---|---|---|
| Apple 10-K | 87 | 26 (30 %) | 13,649 / 50,220 (27 %) |
| Apple 10-Q | 48 | 16 | 7,754 / 22,310 |
| JPM 10-K | 961 | 271 (28 %) | 109,139 / 304,229 (36 %) |
| JPM 10-Q | 552 | 263 (48 %) | 95,622 / 187,237 (51 %) |

Caption shape (heuristic: the nearest non-empty `<p>`/`<div>` before a ≥ 40-char table):

| filing | tables | caption ends with `:` | caption has "following table"/"as follows"/"summar…" | row 0 has a year and no figures |
|---|---|---|---|---|
| aapl-20250927 | 54 | 29 | 26 | 31 |
| jpm-20251231 | 321 | 17 | 151 | 137 |
| jpm-20260630 | 250 | 12 | 117 | 76 |

JPM often puts the table title **inside** the table as row 0 (`Financial performance of
JPMorganChase` in a `colspan=9` cell, then `Year ended December 31, (in millions, ...)`, then
`2025 | 2024 | Change`). Apple puts it in the paragraph before. A caption heuristic has to cover both.

---

## 2. How established libraries split large tables

### 2.1 Unstructured: `repeat_table_headers` exists, but only for `<thead>`/`<th>`

Source: `unstructured/chunking/base.py` and `unstructured/common/html_table.py` at
`e2f22b38` (https://github.com/Unstructured-IO/unstructured/blob/e2f22b38b995858c870dade7cbf6fcf06a9c801c/unstructured/chunking/base.py).

- Options docstring (`base.py:136-145`): *"`repeat_table_headers` Default: `True`. When `True`,
  repeated table-header behavior is enabled for chunked table continuations."* and
  *"`isolate_table` Default: `True`. When `True`, `Table` and `TableChunk` elements are always staged
  in their own pre-chunk and never combined with adjacent non-table elements."*
- Header rows = leading rows where `row.is_header` (`base.py:1076-1087`). `is_header` is *"True for a
  `<thead>` row or an all-`<th>` row after optional blank corner cells"* (`html_table.py:306-308`,
  computed at `:183-197`).
  **On SEC HTML (0 `<th>`, 0 `<thead>`) the detected header count is 0 and nothing repeats.**
- Guard against expensive headers (`base.py:2455-2470`): repeat only if one header row ≤ half the
  window, all header text ≤ ¾ of it, header markup ≤ 4× the window, and no oversized body cell would
  be starved.
- Continuation chunks get `metadata.is_continuation = True` and
  `metadata.num_carried_over_header_rows` (`base.py:1145-1148`). The table text used for embedding
  is `" ".join(table.itertext())` (`html_table.py:273-278`): whitespace-joined with **no cell
  separators**, and empty cells vanish.
- History (CHANGELOG, https://github.com/Unstructured-IO/unstructured/blob/main/CHANGELOG.md):
  `repeat_table_headers` added in 0.22.10, `isolate_tables` toggle in 0.22.30, `<thead>`/`<th>`
  preservation and default-on repetition in 0.27.8.
- Captions: `chunk_by_title` starts a new chunk at `Title` elements and isolates tables
  (`unstructured/chunking/title.py:23-116`). I found no code that copies a preceding paragraph into
  table chunks. With `isolate_table=True` the caption stays in the preceding chunk, which is the same
  defect Lumen has.

### 2.2 Docling: `HybridChunker.repeat_table_header=True`, but the default serializer has no header lines

Source: `docling_core/transforms/chunker/hybrid_chunker.py` and `hierarchical_chunker.py` at
`0d5a7bdc` (https://github.com/docling-project/docling-core/blob/0d5a7bdc6eec6138055714756277730f1520ca61/docling_core/transforms/chunker/hybrid_chunker.py).

- `repeat_table_header: bool = True`, `omit_header_on_overflow: bool = False` (`hybrid_chunker.py:73-75`).
- `segment()` (`:280-337`): when a chunk is a single `TableItem`, it calls
  `table_serializer.get_header_and_body_lines(...)` and uses the header as a prefix on every
  segment. *"Any preamble text that precedes the first table row (e.g. a caption) is included in the
  prefix to account for its token cost, but is stripped from all segments after the first so it
  does not repeat."* So Docling **repeats the header but deliberately not the caption**.
- The base `get_header_and_body_lines` returns `[]` headers (`serializer/base.py:81-108`). Only the
  Markdown serializer overrides it, taking the one row above the `| - |` separator
  (`serializer/markdown.py:712-742`). **The chunking default is `ChunkingDocSerializer`, whose
  `table_serializer = TripletTableSerializer()` (`hierarchical_chunker.py:149-152`) does not
  override it.** So with defaults no header is repeated, because every triplet line is
  self-describing anyway (next item).
- `TripletTableSerializer` output (`hierarchical_chunker.py:116-133`):
  `f"{rows[i]}, {cols[j]} = {value}"` joined with `". "`, with the caption serialized first
  (`:81-86`). This is Docling's built-in "row-as-key-value" format (§3).
- Docling's HTML backend marks a row as column header only if it has no `<td>`
  (`docling/backend/html_backend.py:2024-2036` at `21588f52`,
  https://github.com/docling-project/docling/blob/21588f5261d0a5a051102356d9babb35e790f33e/docling/backend/html_backend.py).
  So SEC tables again get no header. Captions come only from `<caption>` (`:3429-3440`) or
  `<figcaption>`, and SEC filings have neither.

### 2.3 LlamaIndex: tables kept whole, LLM-written summary/caption

Source: `llama-index-core/.../node_parser/relational/base_element.py`
(https://github.com/run-llama/llama_index/blob/main/llama-index-core/llama_index/core/node_parser/relational/base_element.py).

- `MarkdownElementNodeParser` / `UnstructuredElementNodeParser` call an LLM per table with
  `DEFAULT_SUMMARY_QUERY_STR` (`:26-31`): *"What is this table about? Give a very concise summary
  (imagine you are adding a new caption and summary for this table), and output the real/existing
  table title/caption if context provided."*
- The table becomes an `IndexNode` (summary + column descriptions) pointing at a `TextNode` whose
  text is `table_summary + "\n" + table_md` (`:418-475`). **The table isn't split, so there's no
  header repetition.**
- Caption context heuristic (`:170-189`, `:198-220`): use the last 3 lines of the previous text
  element **only if a line starts with "table"**. SEC captions ("The following table shows…",
  "Operating expenses … were as follows") would not match.

### 2.4 LangChain: keep the table intact

`HTMLSemanticPreservingSplitter(elements_to_preserve=["table"])` swaps a table for a placeholder and
reinserts it whole (`langchain_text_splitters/html.py:626-699`, `:951-955`, `:1082-1095` at
`b060a746`, https://github.com/langchain-ai/langchain/blob/b060a7466482ed5dac5fa1ef12e3ad21cccce385/libs/text-splitters/langchain_text_splitters/html.py).
`max_chunk_size` is documented "with allowance for exceeding" (`:646`). It doesn't split tables or
repeat headers.

### 2.5 Microsoft: Azure guidance + `sample-app-aoai-chatGPT`

- Azure Architecture Center, RAG chunking phase
  (https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/rag/rag-chunking-phase, 2026-05-27):
  only generic advice, *"Reformat tables so that they're more easily processed"*, plus language
  model augmentation to *"generate … summaries of tables that become chunks"*. Nothing on headers
  or captions.
- Document Intelligence RAG concept
  (https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/concept/retrieval-augmented-generation):
  the Layout model's Markdown output lets you *"create specific chunks for tables"*. No split
  guidance.
- Azure AI Search, chunk documents
  (https://learn.microsoft.com/en-us/azure/search/vector-search-how-to-chunk-documents): suggests
  you *"append the document title to chunks from the middle of the document to prevent context
  loss"*. That's the same idea as Lumen's context header. Nothing table-specific.
- **`microsoft/sample-app-aoai-chatGPT` `scripts/data_utils.py`** at `ab483f8d`
  (https://github.com/microsoft/sample-app-aoai-chatGPT/blob/ab483f8d0edd5c65272cdc0ef7b92ed4decb678e/scripts/data_utils.py)
  is the closest working analogue of A+B:
  - `split_text` (`:147-173`) takes `table_caption_prefix = self.extract_caption(final_chunks[-1])`,
    the chunk **before** the table.
  - `extract_caption` (`:91-118`) = last title/section heading + `lines[-1]`, i.e. the last line of
    the preceding text.
  - `chunk_table` (`:208-245`): if caption+table fits, emit one chunk. Otherwise split by `<tr>`,
    and every mini-table starts with `"\n".join([caption, "<table>", headers])`, where `headers` is
    `re.search("<th.*>.*</th>", table)`. **So both caption and header go into every piece.** Header
    detection is again `<th>`-only.

### 2.6 Microsoft.Extensions.DataIngestion (preview): row 0 repeated

`ElementsChunker` at `453a10cd`
(https://github.com/dotnet/extensions/blob/453a10cdb8441807c5580fee682c6b5b7ec8f26f/src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/ElementsChunker.cs):

```csharp
// :82-86
AddMarkdownTableRow(table, rowIndex: 0, ref tableBuilder);
AddMarkdownTableSeparatorRow(columnCount: table.Cells.GetLength(1), ref tableBuilder);
int headerLength = tableBuilder.Length;
// :121-124  on overflow
Commit();
// Erase previous rows and keep only the header.
tableBuilder.Length = headerLength;
```

It assumes **row 0 is the header**, throws if header + context ≥ budget (`:88-92`), and works on
`IngestionDocumentTable` (a 2-D `Cells` array, `IngestionDocumentElement.cs:166-180`). Latest
package is `10.10.0-preview.1.26459.2` (https://api.nuget.org/v3-flatcontainer/microsoft.extensions.dataingestion/index.json).
It's a useful .NET reference shape (structured cells plus a header prefix). "Row 0" would be the
empty spacer row on SEC tables.

### 2.7 Semantic Kernel `TextChunker`

No table awareness. It's line/paragraph splitting on `"\n", ".", "?!", ";", ":", ","…`
(`TextChunker.cs:55-56` at https://github.com/microsoft/semantic-kernel/blob/main/dotnet/src/SemanticKernel.Core/Text/TextChunker.cs),
with an optional `chunkHeader` "prepended to each individual chunk" (`:84`, `:109-112`). Lumen
passes `null` (`Chunker.cs:61`).

---

## 3. Serialization formats: what the evidence says

| study | task | finding | relevance to Lumen |
|---|---|---|---|
| Sui et al., *Table Meets LLM*, WSDM 2024 (https://arxiv.org/abs/2305.13062, html v5) | LLM structural understanding, GPT-3.5 | *"overall accuracy gets highest when using the HTML markup language"*. 7-task average (my average of their Table 2): HTML 62.4 %, XML 56.5, JSON 55.8, NL+sep 55.7, Markdown 54.7 | generation side only; not retrieval |
| TQA-Bench (https://arxiv.org/html/2411.19504) | multi-table QA, 7-8B models | *"Markdown consistently yields better performance than other formats for most LLMs"*. *"CSV is the most token-efficient format, while HTML requires nearly three times more tokens"* (8K: MD 5.4k, CSV 3.7k, JSON 5.8k, HTML 10.5k tokens) | token cost: Lumen's pipe format is close to CSV |
| Bhandari et al. 2026 (https://arxiv.org/abs/2604.24040) | dense table retrieval | CSV/TSV/HTML/Markdown/DDL *"can produce substantially different embeddings and retrieval results"*; a centroid over formats beats any single one | format matters for embeddings, but there's no winner |
| TEmBed-T 2026 (https://arxiv.org/pdf/2607.24130) | table embedding benchmark | CSV vs Markdown direction varies by task, *"no format uniformly preferable"* (via search summary; not read in full) | same |
| Wang et al. 2022 (https://arxiv.org/abs/2205.09843) | table retrieval, NQ-table | structure matters in < 30 % of cases; plain DPR on "properly linearized tables" beats table-specific DTR | linearization quality > structure-aware models |
| TableRAG (Chen et al. 2024, https://arxiv.org/html/2410.04739) | million-token tables | encoding **column-value pairs** as retrieval units: cell recall 85.4/87.4 vs 62.8/52.7 for row/column encoding (ArcadeQA/BirdQA) | supports per-cell `column: value` units for numeric lookup |
| Jimeno-Yepes et al. 2024, *Financial Report Chunking for Effective RAG* (https://arxiv.org/abs/2402.05131, html v3) | FinanceBench, 141 Qs / 80 reports | element-based chunks; *"a new chunk is started, preserving the entire table"*; the table prefix metadata uses the description *"typically identified in the table caption"*. Manual QA accuracy: Base-512 48.23 % vs element-based 53.19 % (Keywords / Prefix & Table Description); page retrieval accuracy 67.38 % for the table-description variant vs 68.09 % Base-512 | the only financial evidence; tables never split, no header-repeat ablation |
| Docling `TripletTableSerializer` (§2.2) | library default | `row, column = value` | an existing implementation of option C |

Honest summary: no primary source measures **header repetition on split tables** or
**caption-prepending** in isolation. Format studies disagree, and most measure LLM reading, not
embedding retrieval. TableRAG and Docling's choice of triplets are the closest support for
self-describing cells. Lumen's own eval harness is the only measurement that will settle it for
this corpus.

---

## 4. Caption / title attachment heuristics in the wild

| library | caption source | attached to every split piece? |
|---|---|---|
| Docling | `<caption>`/`<figcaption>` (HTML), layout model (PDF) | **no**, first segment only (`hybrid_chunker.py:286-292`) |
| Unstructured | none for tables in chunking | n/a (table isolated) |
| LlamaIndex | previous element's last 3 lines if one starts with "table", then an LLM summary | whole table, one node |
| MS sample-app | last heading + last line of the preceding chunk | **yes** (`data_utils.py:217`, `:232`) |
| Jimeno-Yepes 2024 | table description from caption (document-understanding model) | tables never split |

For SEC HTML the measurable signal (§1.5): about half of Apple's tables are preceded by a paragraph
ending in `:`, and about 47 % of JPM's by one containing "following table"/"as follows"/"summar…".
JPM often has the title as row 0 inside the table. A practical rule (judgement, untested):
caption = the nearest preceding `Paragraph` element, if it is ≤ ~60 words and (ends with `:` or
matches `following table|as follows|were as follows|summar(y|izes)`), plus the current section
heading. This is close to the MS sample's last-heading + last-line rule.

---

## 5. Header-row detection without `<th>`

The only SEC-aware implementation found is **edgartools** at `0f328ef2`
(https://github.com/dgunning/edgartools/blob/0f328ef2013270c28cc3322ff34b6a524296c8dd/edgar/documents/strategies/table_processing.py):

- `_is_header_row` (`table_processing.py:625-790`):
  - `<th>` → header.
  - A row with ≥ 2 different years and no figures (`carries_figures = has_currency or
    has_large_numbers`, `:678`) → header, unless "total" is in the first 20 chars or a cell reads
    as prose (`:680-696`).
  - ≥ 2 cells that are exactly a year, or a year plus "june 30"/"december 31" → header (`:700-721`).
  - A period-phrase match with no data pattern (`\$\s*\d|\d+(?:,\d{3})+|\d+\.\d+|…|\(\d…\)`) →
    header (`:723-743`).
  - `\(in\s+(?:millions|thousands|billions)\)` → header (`:745-748`).
  - Quarter/month/"ended"/"three months" keywords with no data → header (`:750-781`).
  - The comments document real regressions (UnitedHealth debt schedule: *"35 of the table's 40
    rows were classified as headers"*, `:666-673`). Year heuristics need figure vetoes.
- Multi-row headers: after a header, a row containing years following "Year(s) ended" is also a
  header (`:261-277`). Header detection continues until data rows start.
- Grid: `TableMatrix.build_from_rows` places cells by colspan/rowspan (`edgar/documents/utils/table_matrix.py:46-70`),
  then removes spacing columns while keeping any column with header content (`:393-476`).

For Lumen's samples a simpler rule fits:

- Header block = leading non-empty rows before the first row that has a label cell **and** a
  numeric cell.
- Plus any row that is only years/periods/"(in millions…)"/"Change".
- Spacer rows (all empty) are skipped.

That gives `2025 | Change | 2024 | Change | 2023` for Apple, and `Year ended December 31, (in
millions…)` + `2025 | 2024 | Change` for JPM (plus the title row `Financial performance of
JPMorganChase`, which is better treated as a caption). The JPM `Name | Title | Adoption date | …`
row has no numbers and is caught by the "before first numeric data row" rule. Not validated across
the corpus; see §8.

**Row-group labels** are a second header level SEC tables use heavily: `Gross margin:`,
`Capital ratios - Standardized(a)`, `Selected ratios and metrics`. These are label-only rows inside
the body. `g-cet1-q2-26` shows the cost: `Capital ratios - Standardized(a)` ended chunk 47, and its
rows started chunk 48. Carrying the **last label-only row** into a continuation chunk (alongside
the header) is cheap, but I found no library that does it. It's an original idea, so treat it as a
hypothesis.

**Header inheritance across sibling tables** (`a-gm-pct-fy25`): Apple's percentage table has the
same 18-column grid as the dollar table directly above it, has no header of its own, and the only
thing between them is nothing. A rule "headerless table immediately following a table with an
identical grid width inherits its header rows" would label `46.9` with `2025`. Also original;
risky on JPM, where adjacent tables are common.

---

## 6. Empty / `$` / `%` / `)` cell merging

- **edgartools** `CurrencyColumnMerger`
  (https://github.com/dgunning/edgartools/blob/0f328ef2013270c28cc3322ff34b6a524296c8dd/edgar/documents/utils/currency_merger.py,
  `:12-27`): *"SEC filings often split currency values into two cells: Cell 1: "$" … Cell 2:
  "224.11" … This class detects this pattern and merges them into "$224.11""*. It works column-wise:
  a symbol column needs ≥ 60 % of its non-empty, non-header cells to be symbols (`:93`), and the
  value column ≥ 60 % numeric (`:125`). Wiring: `edgar/documents/table_utils.py:44-59`.
- edgartools `TableMatrix` consolidation (`table_matrix.py:505-517`):
  - `prev == '$' and curr[0].isdigit()` → currency.
  - `prev.startswith('(') and curr == ')'` → parentheses (negative).
  - `curr == '%' and prev[-1].isdigit()` → percentage.
- Unstructured doesn't merge. Its text is whitespace-joined, so `$ 112,887` reads naturally but
  column boundaries are lost (`html_table.py:273-278`).
- Docling: not checked for `$` merging (see §8).

For Lumen, a **row-wise** merge on the grid is enough, and safer than column statistics:

- Attach `$` to the next non-empty cell.
- Attach `%` / `)%` / `)` to the previous one.
- Keep empty cells as positional slots in the grid.
- Then label by the header cell whose colspan covers the slot.

On the Apple grid this gives `Research and development | 2025: $34,550 | Change: 10% | 2024: $31,370 | …`
and `Percentage of total net sales | 2025: 8% | 2024: 8% | 2023: 8%`. That removes the column
shift, because the empty Change slots keep their position.

---

## 7. Token budget and embedding effect

Measured header and caption costs (local DB `TokenCount`, cl100k):

- Apple heading + caption chunk #170: **34 tokens** (`Gross Margin` + the "were as follows
  (dollars in millions):" line). That's a typical caption.
- Header rows: `2025 | Change | 2024 | Change | 2023` is about 12-15 tokens. A JPM 2-3-row header
  is about 25-40 (estimate, chars/4).

Cost of A (header repeat) in the 250 budget: about 15-40 tokens displaced per continuation chunk.

- Apple 10-K: 26 continuations × ~20 ≈ 500 tokens (≈ 1 % of 50 k).
- JPM 10-Q: 263 × ~30 ≈ 8 k (≈ 4 % of 187 k).

Because the budget is fixed, this shows up as **more chunks** (an estimated +10-15 % table chunks
on JPM). Neither embedding nor prompt tokens per chunk change.

- B (caption in text) adds about 20-40 tokens to *every* table chunk, not just continuations. On
  JPM that's about 1,500 table chunks × 30 ≈ 45 k tokens, i.e. more chunks again. Putting the
  caption in `ContextHeader` instead keeps `Text` within 250, but the caption then never reaches
  the `/api/ask` model (`RagPrompt.cs:25-30`). That's exactly the `g-dimon-10b5-1` failure mode
  (retrieved at rank 2, refused because the "Rule 10b5-1" words weren't in the source text).
- C (`label, column: value` per cell) repeats the row label and column header for every cell.
  Estimate 2-3× table tokens: Apple 10-K table text 13.6 k → ~30-40 k; JPM 10-Q 96 k → ~200-280 k.
  That roughly doubles chunk count and embedding cost for table-heavy filings. Embedding cost on
  `text-embedding-3-small` is still small in money terms, but retrieval sees **many more
  near-duplicate table chunks** competing for top-10.
- Embedding-quality tradeoff: none of the sources quantify "header tokens dilute the vector".
  Unstructured's guard (`base.py:2455-2470`, header ≤ ¾ of window) and Docling's
  `omit_header_on_overflow` are engineering limits, not measured optima. The Stage 4 b2 lesson is
  relevant. The identical context header on every chunk *raised* hit@10 but *lowered* hit@1
  (`../discussion/stage-4-hands-on-findings.md`, b2 row). Repeated identical text makes sibling
  chunks look alike. Expect the same risk for repeated headers and captions across the continuation
  chunks of one table.

---

## 8. Options for Lumen

Common prerequisites for every option except the narrow A-only variant:

- **R. Structured table element.** Extend `DocumentElement` (Application) with an optional table
  payload: rows of cells `{text, colspan}`, `headerRowCount`, `caption?`. Populate it in
  `HtmlDocumentReader` (spacer rows kept to define the grid). `PdfDocumentReader` sets
  `headerRowCount = 0`, so no behaviour change. `Text` keeps today's serialization when every
  switch is off, so the `off` profile stays byte-identical.
- The integration test host must keep new switches **off**, as it does for `ContextHeaders` and
  `ProtectDecimals`, because Fake embeddings match only byte-identical text.
- Each switch needs: a `ChunkingOptions` property, a validator entry, a profile token in
  `IngestProfileProvider.cs:8`, a re-upload, and a `/api/settings` value.
- Reader-side changes also alter `Document.Content` (`UploadDocumentCommand.cs:141`), so the
  profile must cover the reader too. Today it names only chunker settings.

### A: repeat the header block in continuation chunks

- **What:** in `SplitTableIfOversized`, prefix every group after the first with the detected header
  rows. Optionally (A′) also carry the last label-only row-group line (`Capital ratios -
  Standardized(a)`). Use a Unstructured-style guard: skip if the header is > ½ the budget.
- **Should fix:** `g-cet1-q2-26` (chunk 48 gains `Three/Six months ended June 30, … 2026 | 2025 | Change`
  and, with A′, `Capital ratios - Standardized`), `g-tbvps-delta` (10-Q #27; still needs a 10-K/10-Q
  pair in top-10), and better ranks for the anchors of `g-cet1-2025`, `g-eps-2025` and
  `g-net-income-2025`. **It does not fix** `a-gm-pct-fy25`, `a-china-fy25`, `a-rnd-fy25` or
  `g-dimon-10b5-1` (§1.5).
- **Tokens:** +15-40 per continuation chunk; about +1 % (Apple) to +4 % (JPM 10-Q) tokens; more chunks.
- **Profile:** `thdr=v1|off` (A′ = `thdr=v2`).
- **Risks:**
  - Header misdetection: a data row repeated as a header, or a lost first data row. edgartools'
    regression notes show it is easy to over-detect.
  - The b2 effect: near-identical openings across sibling chunks lowering hit@1.
  - A narrow variant without R is possible (detect the header from the pipe text by "leading lines
    without digits-with-commas"), but it inherits the column-shift ambiguity.

### B: caption into every table chunk's text

- **What:** at chunk time, when a `Table` follows a `Paragraph` that matches the caption rule (§4),
  move it (or copy it, see risks) to the front of every table chunk:
  `Caption: <text>\n<header>\n<rows>`. For JPM, a title row 0 (single wide cell, no digits) acts as
  the caption.
- **Should fix:** `g-dimon-10b5-1` (the "Rule 10b5-1 trading arrangements" words land in the chunk
  the model sees). Likely better ranks for `a-china-fy25` ("net sales by reportable segment") and
  `a-rnd-fy25` ("Operating expenses"). `a-gm-pct-fy25` only partially: the caption names "gross
  margin percentage" but there are still no years.
- **Tokens:** +20-40 per table chunk (≈ +45 k tokens on JPM 10-K); more chunks.
- **Profile:** `tcap=v1|off`.
- **Risks:**
  - *Copy* vs *move*: moving the caption out of the prose chunk could lose prose-only answers;
    copying duplicates text.
  - False captions: the last line of a long paragraph.
  - Putting it in `ContextHeader` instead would **not** fix Dimon unless `RagPrompt` also emits the
    context header. That's a prompt change with its own `promptHash` shift, so keep the two apart.

### C: per-cell key-value serialization

- **What:** serialize each data row as `Row label | col header: value | …`, or Docling-style
  `Row label, col header = value`, using the grid from R plus D. Example:
  `CET1 capital ratio | Standardized, JPMorgan Chase & Co.: 14.2% | Standardized, JPMorgan Chase Bank, N.A.: 15.0% | …`.
  Optional row-group prefix (`Capital ratios - Standardized › CET1 capital`).
- **Should fix:**
  - Every column-ambiguity case: `g-cet1-q2-26` (#1127 at rank 4 already holds the number but under
    a 2-level header), `g-cet1-2025`, and `a-gm-pct-fy25` only with header inheritance (§5).
  - It makes A largely redundant, since each cell carries its header.
- **Tokens:** about 2-3× table text (Apple 10-K 13.6 k → ~30-40 k; JPM 10-Q 96 k → ~200-280 k).
  Roughly 2× chunk count on JPM.
- **Profile:** `tser=kv1|pipe`.
- **Risks:**
  - The biggest change, so the most room for noise (Stage 4 showed ±9 pp swings from header-text
    changes alone).
  - Eval anchors that span cell text (e.g. `capital ratio - Standardized`, already unmatched) need
    re-checking. The harness marks unlabelable questions, so watch that count.
  - More near-duplicate table chunks competing for `ContextTopK = 10`.
  - The decimal guard must still run on the new text.

### D: reader cell normalization (`$`, `%`, `)`, empty slots)

- **What:** in `HtmlDocumentReader.ExtractTableText`, compute the colspan grid. Merge `$` → next
  cell and `%`/`)`/`)%` → previous cell (edgartools rules, row-wise). Keep empty slots as `""` where
  a header column exists, or drop them if no header covers the slot. Output pipe format:
  `Research and development | $34,550 | 10% | $31,370 | 5% | $29,915` and
  `Percentage of total net sales | 8% | | 8% | | 8%`.
- **Should fix:** no named question on its own. It's the enabler for C and for column-accurate A.
  It shortens table text a bit (fewer `|` tokens) and removes the "`(4) | %`" split that NumberScorer
  and LLM answers have to read around.
- **Tokens:** slightly **fewer** (each `$`/`%` merge saves about 2 tokens: `" | "` + symbol boundary).
- **Profile:** `cells=v1|off`. This is a **reader** setting; it changes `Document.Content`.
- **Risks:**
  - Kept empty slots add `| |` tokens.
  - Footnote-marker cells (`(a)`) could merge wrongly.
  - A different `TableText` invalidates the current anchor/chunk distribution, so it needs a
    re-baseline run before A/B/C.

### E (optional): header inheritance for headerless sibling tables

- **What:** a table with no detected header and the same grid width as the table immediately
  before it (no prose in between) inherits that table's header rows.
- **Should fix:** `a-gm-pct-fy25` (`Total gross margin percentage | 2025: 46.9% | …`), and the 10-Q
  twin `a-gm-pct-delta` (holdout).
- **Tokens:** negligible.
- **Profile:** folded into `thdr=v2`.
- **Risks:** false inheritance on JPM, where stacked tables are common. Only safe with an
  identical-grid check. Original heuristic, no prior art found.

### What to measure first (judgement, not evidence)

1. **Re-baseline after R + D** (all switches off except D) on retrieval + full. D changes table text
   for every filing, so later steps need their own baseline.
2. **A′+B as one step** (`thdr=v2;tcap=v1`). They are cheap, they target the two refusals that
   exist (`g-cet1-q2-26` in the working set, `g-dimon-10b5-1` in the holdout), and they match what
   the MS sample does. Watch hit@1 (b2 risk), the `table-value` category, and chunk count. If the
   flip count is too small to read (likely, n = 30), report per-question ranks for the
   continuation-anchored questions listed in §1.5, rather than overall accuracy.
3. **C** as a separate switch against step 2, given its token/chunk cost and the anchor risk.
4. E only if `a-gm-pct-fy25`'s rank still matters after C.

The eval set has very few questions whose answer lives in a continuation chunk (about 4 working +
1 holdout). Any of these steps can easily come out "no significant difference". Consider adding
3-5 continuation-anchored JPM questions **before** measuring, following the Stage 4 rule that
questions are written before optimizing.

---

## 9. Open questions / not verified

- Continuation counts in §1.5 use a heuristic (previous chunk is a ≥ 200-token table chunk with
  the same context header). They were not recomputed by re-running the chunker. Two adjacent
  different tables can be counted as a continuation.
- The "> 250 tokens" table counts in §1.2 use chars/4, not cl100k. JPM header-cost estimates are
  also chars/4.
- The caption-shape statistics use a crude "nearest preceding `<p>`/`<div>`" walk, not Lumen's
  element stream.
- Header detection rule in §5 is not validated across all 1,500+ JPM table chunks. edgartools'
  regressions suggest it will misfire on debt schedules and date-range tables.
- JPM header rows don't always span the full grid width (e.g. `2025|2024|''|Change` with colspan 3
  each = 12 columns vs 15 in data rows). Grid alignment may need right-alignment or an
  offset rule. Not investigated.
- Docling `$`/`%` handling and its dataframe export for header-less tables (which row becomes the
  column names when no cell is `column_header`) were not checked.
- Unstructured `partition_html` captions: I didn't check whether a `FigureCaption`/`Title` element
  is ever emitted for SEC table captions.
- TEmBed-T (arXiv 2607.24130) was seen only through a search summary. No numbers were taken from it.
- Jimeno-Yepes et al. Discussion section says "50% vs 53.19%", which doesn't match their Table 5
  (51.77). Numbers above are from the tables.
- Whether to also emit `ContextHeader` into the `/api/ask` prompt is a separate question. It would
  shift `promptHash` and is not a chunking switch.
- sec-parsers / other EDGAR libraries were not examined. edgartools was the only SEC-specific
  source read.
