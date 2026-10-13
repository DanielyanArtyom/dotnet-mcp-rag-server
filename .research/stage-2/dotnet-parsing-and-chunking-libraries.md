# .NET tooling for HTML/PDF/XBRL parsing and text chunking

Sources current as of **2026-09-13**.

Scope: what OSS .NET tooling actually exists for parsing SEC EDGAR 10-K/10-Q filings (HTML, possibly inline XBRL) and for chunking the extracted text. Does not cover SEC EDGAR format specifics or chunking theory (size/overlap/sentence-boundary strategy) — other research files cover those.

## Package/version summary table

| Package | Purpose | Latest version | Publish date | License |
|---|---|---|---|---|
| `HtmlAgilityPack` | HTML DOM parsing, tolerant of malformed markup | 1.13.0 | 2026-08-24 | MIT |
| `AngleSharp` | Spec-compliant HTML/CSS/DOM parsing | 1.8.1 | 2026-09-10 | MIT |
| `PdfPig` | PDF text/content extraction (pure .NET) | 0.1.16 (pre-1.0, API not stable) | 2026-08-22 | Apache-2.0 |
| `Diwen.Xbrl` / `Xbrl` (TimHanewich) / `Gepsio` (`JeffFerguson.Gepsio`) | XBRL instance document parsing | varies, see below | varies | varies |
| `Microsoft.SemanticKernel.Core` (namespace `Microsoft.SemanticKernel.Text`, class `TextChunker`) | Token/line/paragraph text chunking | 1.80.1 | 2026-09-03 | MIT |
| `Microsoft.Extensions.DataIngestion` (+ `.Abstractions`, `.MarkItDown`, `.Markdig`) | Full ingestion pipeline: readers, chunkers (header/section/semantic), enrichers, vector-store writers | 10.4.0-preview.1.26160.2 | — | MIT (Microsoft `dotnet` org) |
| `Microsoft.ML.Tokenizers` | Token counting (BPE, Tiktoken, Llama, RoBERTa, Phi2) | 2.0.0 | 2025-11-11 | MIT |
| `PragmaticSegmenterNet` | Rule-based sentence-boundary detection, 21 languages | 1.0.5 | — | MIT |

## 1. HTML parsing

### HtmlAgilityPack

- NuGet page: latest **1.13.0**, published 2026-08-24, **MIT** license. [nuget.org/packages/HtmlAgilityPack](https://www.nuget.org/packages/HtmlAgilityPack)
- Its own description: *"Html Agility Pack (HAP) It is an agile HTML parser that builds a read/write DOM and supports plain XPATH or XSLT... The parser is very tolerant of 'real world' malformed HTML."*
- GitHub README ([zzzprojects/html-agility-pack](https://github.com/zzzprojects/html-agility-pack)): *"The object model is very similar to what proposes System.Xml, but for HTML documents"* — i.e. `HtmlDocument` → `DocumentNode` → `HtmlNode` tree, traversed via `SelectNodes`/`SelectSingleNode` (XPath) or plain LINQ over `.Descendants()`.
- Explicit, repeated claim of malformed-HTML tolerance — this is the library's whole reason to exist (it does not enforce a spec; it just tries to build *some* DOM out of whatever tag soup it's given).

### AngleSharp

- NuGet page: latest **1.8.1**, published 2026-09-10, **MIT** license. [nuget.org/packages/AngleSharp](https://www.nuget.org/packages/AngleSharp). Targets `.netstandard2.0`, `net8.0`, `net10.0`.
- Own description: *"a .NET library that gives you the ability to parse angle bracket based hyper-texts like HTML, SVG, and MathML."*
- GitHub README ([AngleSharp/AngleSharp](https://github.com/AngleSharp/AngleSharp)): *"The included parser is built upon the official W3C specification. This produces a perfectly portable HTML5 DOM representation of the given source code and ensures compatibility with results in evergreen browsers."*
- It states its own differentiator against the other library by name: *"The advantage over similar libraries like HtmlAgilityPack is that the exposed DOM is using the official W3C specified API, i.e., that even things like `querySelectorAll` are available in AngleSharp."*
- Practical difference: AngleSharp gives a browser-accurate DOM (`querySelector`/`querySelectorAll`, real `Document`/`Element` types matching the DOM spec) at the cost of being a stricter, spec-driven parser; HtmlAgilityPack gives a looser, more XPath/System.Xml-flavored model but was purpose-built to shrug off malformed markup.

### Comparison for messy real-world documents

Neither library's own docs make a quantified claim about which "survives" worse-formed HTML better — HtmlAgilityPack's is the more explicit, repeated marketing claim ("very tolerant of real world malformed HTML"); AngleSharp's pitch is spec-compliance and a richer DOM API, not fault tolerance per se, though its HTML5 parser is required to implement the WHATWG/W3C HTML5 parsing algorithm, which itself has well-defined error-recovery rules (browsers must render broken markup too, so the spec dictates recovery behavior) — so it is not fragile on malformed input either, just via a different mechanism (following the spec's own recovery algorithm) rather than an intentionally forgiving ad hoc parser.

## 2. PDF parsing

### PdfPig

- NuGet page: latest **0.1.16**, published 2026-08-22, **Apache-2.0** license. [nuget.org/packages/PdfPig](https://www.nuget.org/packages/PdfPig)
- Own description: *"PdfPig supports reading text and content from PDF files. It also supports basic PDF file creation."*
- GitHub README ([UglyToad/PdfPig](https://github.com/UglyToad/PdfPig)) explicitly warns against the naive API: *"You should not use `page.Text` directly, unless you know what you're doing"* and points users toward its layout-analysis/word-extraction utilities instead — i.e. raw `page.Text` concatenates PDF content-stream text runs in stream order, which does not track visual reading order (columns, tables) and needs the library's positional/word-clustering helpers to be usable for chunking.
- Started as a C# port of Apache PDFBox: *"This project started as an effort to port PDFBox to C#... wouldn't be possible without the work done by the PDFBox team and the Apache Foundation."*
- No dedicated table-detection API is mentioned in the README — table structure would have to be reconstructed manually from word/glyph bounding-box positions (the library exposes those), not read off a built-in table model.
- Still pre-1.0: *"While the version is below 1.0.0 minor versions will change the public API without warning (SemVer will not be followed until 1.0.0 is reached)."* — a real constraint if PDF ingestion becomes a long-lived Stage 2/3 dependency.
- No other actively-relevant pure-OSS .NET PDF text-extraction library surfaced in this research pass as a clear alternative to PdfPig; the commercial space (iText 7 AGPL/commercial dual-license, Aspose.PDF, Syncfusion) exists but wasn't in scope since PdfPig already covers plain text extraction and EDGAR filings are primarily HTML.

## 3. XBRL / inline XBRL parsing

No dominant, clearly-maintained OSS .NET library emerged. What NuGet/GitHub actually show:

- **`Xbrl`** (GitHub: [TimHanewich/Xbrl](https://github.com/TimHanewich/Xbrl)) — reads XBRL instance documents via `XbrlInstanceDocument.Create()`, lists facts/context references, has helpers for extracting facts by fiscal period. The README shown gives no explicit license statement and no inline-XBRL mention in the excerpt fetched; maintenance signal (commit count, no open issues) is inconclusive rather than confirming active maintenance.
- **`Diwen.Xbrl`** — described (via NuGet/library aggregator listing, not fetched directly from its own README in this pass) as a .NET library for reading/writing/comparing/converting XBRL reports.
- **Gepsio** (`JeffFerguson.Gepsio`, [JeffFerguson/gepsio](https://github.com/JeffFerguson/gepsio)) — the most substantive-looking option found: a full XBRL document object model, validates loaded documents against the XBRL specification and throws on invalid documents, and states its test suite is built against the official **XBRL Conformance Suite**. Recent releases target .NET 8. This is the one candidate with an explicit claim of spec-conformance testing rather than just "reads some XBRL files."
- **`Manpuku.Edinet.Xbrl`** — a .NET 8 library, but purpose-built for Japan's EDINET XBRL filings (taxonomy/instance/linkbase), not SEC EDGAR; it does claim inline-XBRL (iXBRL) support, which is a data point that iXBRL parsing is achievable in .NET, just not by a library targeting SEC filings specifically.
- **Commercial-only options** exist and are more clearly maintained/documented (Aspose.Finance with `XbrlDocument`/`InlineXbrlDocument` classes; Altova RaptorXML+XBRL Server with a .NET API) but are paid products, out of scope for an OSS portfolio project.

**Bottom line for this topic:** there is no single obviously-correct, actively-maintained, SEC-EDGAR-oriented OSS .NET XBRL library the way HtmlAgilityPack/AngleSharp are the obvious answer for HTML. Gepsio is the most credible general-purpose candidate found (spec-conformance-tested DOM), but none of the options here were verified against a real SEC 10-K inline-XBRL exhibit in this research pass — that would need a hands-on spike before committing. Realistically, if Stage 2 needs anything beyond "extract the human-readable HTML text and ignore the embedded XBRL tags," hand-rolling targeted XML/XPath queries against the small set of us-gaap/dei tags actually needed (rather than adopting a full general-purpose XBRL taxonomy engine) is a legitimate fallback, and given inline XBRL is just HTML with `<ix:*>` tags carrying the machine-readable facts inline, AngleSharp or HtmlAgilityPack can already parse the surrounding document — only the `ix:nonFraction`/`ix:nonNumeric` tag extraction would need bespoke code, not a new parsing engine.

## 4. Text chunking utilities in the .NET/Microsoft AI ecosystem

### `Microsoft.SemanticKernel.Text.TextChunker`

- Confirmed to exist. Its own Microsoft Learn API page ([TextChunker Class](https://learn.microsoft.com/en-us/dotnet/api/microsoft.semantickernel.text.textchunker?view=semantic-kernel-dotnet)) states: Namespace `Microsoft.SemanticKernel.Text`, Assembly `Microsoft.SemanticKernel.Core.dll`, **Package `Microsoft.SemanticKernel.Core` v1.45.0** (that page's snapshot version — current NuGet latest for `Microsoft.SemanticKernel.Core` is **1.80.1**, published 2026-09-03, MIT license, per [its NuGet page](https://www.nuget.org/packages/Microsoft.SemanticKernel.Core)).
- `Microsoft.SemanticKernel.Core`'s own dependency list is lightweight: `Microsoft.Extensions.DependencyInjection`, `Microsoft.SemanticKernel.Abstractions`, `System.Numerics.Tensors` — it does **not** drag in the full SK orchestration/planner/connector stack. `TextChunker` is usable standalone without adopting Semantic Kernel as an orchestration framework.
- Class-level doc comment (quoted verbatim from the API page): *"Split text in chunks, attempting to leave meaning intact. For plain text, split looking at new lines first, then periods, and so on. For markdown, split looking at punctuation first, and so on."*
- Public API surface (four static methods, confirmed from both the Learn API page and the source at [`TextChunker.cs`](https://github.com/microsoft/semantic-kernel/blob/main/dotnet/src/SemanticKernel.Core/Text/TextChunker.cs)):
  - `SplitPlainTextLines(string text, int maxTokensPerLine, TokenCounter? tokenCounter = null)` — "Split plain text into lines."
  - `SplitMarkDownLines(string text, int maxTokensPerLine, TokenCounter? tokenCounter = null)` — "Split markdown text into lines."
  - `SplitPlainTextParagraphs(IEnumerable<string> lines, int maxTokensPerParagraph, int overlapTokens = 0, string? chunkHeader = null, TokenCounter? tokenCounter = null)` — "Split plain text into paragraphs," with built-in **token overlap** and an optional **prepended chunk header** (both directly relevant to a chunk-overlap + provenance-metadata strategy).
  - `SplitMarkdownParagraphs(...)` — same shape, markdown-flavored.
- Documented usage pattern (from the SDK's own sample, [`TextChunkerUsage.cs`](https://github.com/microsoft/semantic-kernel/blob/main/dotnet/samples/Concepts/Memory/TextChunkerUsage.cs)): call the `*Lines` method first with a small max-token budget, then feed those lines into the matching `*Paragraphs` method with a larger max-token budget — a two-pass line→paragraph chunking pipeline, not a single call.
- Known caveat, not from the library's own docs but worth flagging: a filed upstream issue reports a stack overflow in `SplitPlainTextParagraphs()` on very large documents ([microsoft/semantic-kernel#1633](https://github.com/microsoft/semantic-kernel/issues/1633)) — worth a smoke test against a full 10-K's worth of text before relying on it in a pipeline.

### `Microsoft.Extensions.DataIngestion` — a newer, more complete alternative

This surfaced as a more significant finding than the prompt anticipated: Microsoft ships (as of this research date) a **preview-stage** dedicated data-ingestion library, documented at [Data ingestion - .NET | Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/data-ingestion) (page dated 2025-12-02, updated 2026-07-21).

- Packages on NuGet, all **preview**, MIT-licensed, under the `dotnet` org: `Microsoft.Extensions.DataIngestion` (10.4.0-preview.1.26160.2), `.Abstractions`, `.MarkItDown`, `.Markdig`.
- Architecture per its own docs: `IngestionDocument` (a Markdown-centric unified document representation) ← `IngestionDocumentReader` (pluggable readers: MarkItDown, Markdig, with LlamaParse/Azure Document Intelligence "added in the future") → document processors (e.g. `ImageAlternativeTextEnricher`) → **chunkers** → chunk processors (`SummaryEnricher`, `SentimentEnricher`, `KeywordEnricher`, `ClassificationEnricher`, all built on `Microsoft.Extensions.AI.Abstractions`) → `IngestionChunkWriter<T>` / `VectorStoreWriter<T>` (built on `Microsoft.Extensions.VectorData`) → an `IngestionPipeline<T>` that chains all of the above with partial-success semantics (`IAsyncEnumerable<IngestionResult>`).
- Its own docs state three chunking strategies exist: *"Header-based chunking to split on headers... Section-based chunking to split on sections (for example, pages)... Semantic-aware chunking to preserve complete thoughts."* Quoted example instantiates a `HeaderChunker`:
  ```csharp
  Tokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-5");
  IngestionChunkerOptions options = new(tokenizer) { MaxTokensPerChunk = 2000, OverlapTokens = 0 };
  IngestionChunker<string> chunker = new HeaderChunker(options);
  ```
- Its own docs state explicitly: *"These chunking strategies build on the Microsoft.ML.Tokenizers library to intelligently split text into appropriately sized pieces."* — confirming the token-counting layer question directly (see below).
- This library is document-shape-agnostic in principle but is explicitly **Markdown-centric** (`IngestionDocument` "is Markdown-centric because large language models work best with Markdown formatting") — for EDGAR HTML filings, this would mean converting HTML→Markdown first (e.g. via a Markdig-based or MarkItDown reader) rather than feeding raw HTML/DOM nodes into the chunker directly.

### Does `Microsoft.Extensions.AI` itself offer chunking?

No — `Microsoft.Extensions.AI`/`Microsoft.Extensions.AI.Abstractions` (the already-settled abstraction layer over Azure OpenAI per this project's CLAUDE.md) is the model/embedding-client abstraction; it does not itself expose chunking helpers. Chunking lives one layer up, in the separate `Microsoft.Extensions.DataIngestion` family, which *consumes* `Microsoft.Extensions.AI.Abstractions` (for its LLM-backed chunk enrichers) but is a distinct, separately-versioned, still-preview package set.

### `Microsoft.ML.Tokenizers` — token counting

- NuGet page: latest **2.0.0**, published 2025-11-11, **MIT** license. [nuget.org/packages/Microsoft.ML.Tokenizers](https://www.nuget.org/packages/Microsoft.ML.Tokenizers)
- Own description: *"Microsoft.ML.Tokenizers provides an abstraction for tokenizers as well as implementations of common tokenization algorithms."*
- Implements BPE, **Tiktoken** (the family used by GPT-4o-class OpenAI/Azure OpenAI models — directly relevant since Azure OpenAI is the settled embeddings/LLM backend), Llama, English RoBERTa, and Phi2 tokenizers, with an extensible normalizer/pre-tokenizer/model/decoder pipeline. Targets `net8.0` and `.netstandard2.0`.
- This is the library both `TextChunker`'s `TokenCounter` delegate and `Microsoft.Extensions.DataIngestion`'s chunkers are meant to be backed by for accurate token-based (not character-based) chunk-size enforcement against the actual embedding/completion model's tokenizer.

## 5. Sentence-boundary detection in .NET

- **`PragmaticSegmenterNet`** ([github.com/UglyToad/PragmaticSegmenterNet](https://github.com/UglyToad/PragmaticSegmenterNet), NuGet [`PragmaticSegmenterNet`](https://www.nuget.org/packages/PragmaticSegmenterNet), latest **1.0.5**, **MIT** license per its `LICENSE` file) is the one real, purpose-built option found. It's a direct C# port of the Ruby `pragmatic_segmenter` gem — rule-based (not ML-based), with a `Segmenter.Segment(...)` method, claimed support for 21 languages, and options for text cleaning and "document type" handling aimed at exactly this kind of messy-real-world-document use case (its own description calls out handling PDF- and HTML-sourced text with irregular formatting). Notably from the same author/org (`UglyToad`) as `PdfPig`, and targets `net5.0`/`netstandard2.0`.
- No ML.NET-native sentence-boundary-detection component surfaced in this research pass — ML.NET's built-in text components are oriented toward tokenization/featurization for classical ML pipelines (n-grams, word embeddings for training), not NLP sentence segmentation as a standalone utility.
- General finding, not project-specific: sentence boundary detection for standard English prose is treated as a largely solved problem in the wider NLP literature, but every mature solution referenced across languages/frameworks is rule-based or lightly-statistical (Punkt-style), not a heavyweight model — consistent with `PragmaticSegmenterNet` being a reasonable, low-dependency fit rather than a compromise choice.
- A simple regex/heuristic approach (splitting on `. ! ?` followed by whitespace + capital letter, with abbreviation exception lists) remains the realistic fallback/baseline in the .NET ecosystem absent adopting `PragmaticSegmenterNet`, and is what most hand-rolled chunking pipelines in this space actually ship with when a dedicated library isn't pulled in.

## Implication for Lumen

Lumen is on .NET 10 / ASP.NET Core, with `Microsoft.Extensions.AI` + Azure OpenAI already settled as the LLM/embeddings layer (per this repo's `CLAUDE.md`). Given that:

- **HTML parsing**: either `HtmlAgilityPack` or `AngleSharp` is a legitimate, actively-maintained choice for SEC EDGAR HTML filings. Given the project explicitly wants filings as a chunking *stress test* — deliberately messy/table-heavy documents — `AngleSharp`'s spec-driven parsing (real `querySelector`/`querySelectorAll`, a browser-accurate DOM) may make table/section structure easier to walk deterministically than `HtmlAgilityPack`'s looser XPath-oriented model; `HtmlAgilityPack`'s explicit "tolerant of real-world malformed HTML" pitch is the stronger fit if EDGAR HTML turns out to be genuinely broken rather than just complex. This is worth a quick spike against a couple of real 10-K HTML files before committing (the CLAUDE.md's "deliberately not decided yet" section already flags exact SEC filing format handling as something to find empirically, not decide on paper).
- **PDF parsing**: only pull in `PdfPig` if/when a filing actually needs to be sourced as PDF rather than HTML (EDGAR's primary format is HTML/iXBRL, so this may not be needed at all for Stage 2). If it is needed, budget for its pre-1.0 API instability and plan to use its word/layout extraction API, not raw `page.Text`.
- **XBRL**: given no clearly-dominant OSS option exists, the pragmatic Stage 2 path is likely to treat inline XBRL as "HTML with extra `<ix:*>` tags" — parse it with whichever HTML library is already chosen, and hand-write targeted extraction for the specific `us-gaap`/`dei` facts actually needed, rather than adding a general-purpose XBRL DOM library (Gepsio) as a dependency up front. Revisit only if a concrete need for full taxonomy-aware XBRL validation emerges.
- **Chunking**: `Microsoft.SemanticKernel.Core`'s `TextChunker` is a small, low-dependency, immediately usable option (line→paragraph, token-aware via a pluggable `TokenCounter`, with built-in overlap and chunk-header support) that doesn't require adopting Semantic Kernel as an orchestration framework — a good fit for a project that already has its own retrieval/generation pipeline via `Microsoft.Extensions.AI`. `Microsoft.Extensions.DataIngestion` is architecturally the more complete answer (header/section/semantic-aware chunkers, a full pipeline with vector-store writers) but is **preview-only** (10.x preview builds, no GA) and Markdown-centric, meaning it would push the design toward an HTML→Markdown conversion step before chunking — a bigger architectural bet to take on mid-preview for a project whose own conventions favor deciding format-handling empirically rather than upfront.
- **Token counting**: `Microsoft.ML.Tokenizers`'s `TiktokenTokenizer` is the natural token-counter backing whichever chunker is chosen, since it directly targets the OpenAI/Azure OpenAI tokenization family Lumen already depends on for embeddings/generation — plug it in as the `TokenCounter` delegate for `TextChunker`, or as the `Tokenizer` for `IngestionChunkerOptions` if `Microsoft.Extensions.DataIngestion` is adopted instead/later.
- **Sentence boundaries**: `PragmaticSegmenterNet` is a real, low-friction option if sentence-boundary-aware chunking is the direction chosen (the CLAUDE.md's plan explicitly lists "sentence boundaries" as one of the chunking parameters to explore) — small, MIT-licensed, no heavyweight ML dependency, from the same author as the already-vetted `PdfPig`. A regex-based approach remains the fallback if the library proves not worth the dependency in practice.

## Uncertain / could not verify

- Neither `HtmlAgilityPack` nor `AngleSharp`'s own docs make a head-to-head, evidence-backed claim about which handles worse-formed/broken HTML more gracefully in practice — this needs a hands-on spike against actual EDGAR filing HTML, not a documentation-only judgment.
- XBRL: none of `Diwen.Xbrl`, `TimHanewich/Xbrl`, or `Gepsio` were exercised against a real SEC 10-K inline-XBRL exhibit in this pass — license terms for `Diwen.Xbrl` and `TimHanewich/Xbrl` specifically were not confirmed from their own repos/NuGet pages directly (only inferred from secondary listings), and none were checked for whether they parse **inline** XBRL (as embedded in EDGAR's primary HTML documents) versus only standalone `.xbrl` instance documents — that distinction matters a lot for this specific corpus and needs direct verification before any of these libraries are adopted.
- `Microsoft.Extensions.DataIngestion`'s actual API stability/roadmap toward GA (version numbers are on a `10.x-preview` train mirroring .NET 10, but no GA date was found) — treat any code sample from its docs as subject to change.
- The upstream `TextChunker.SplitPlainTextParagraphs()` stack-overflow issue ([#1633](https://github.com/microsoft/semantic-kernel/issues/1633)) — its current status (fixed / still open / workaround) was not checked; worth confirming before relying on it against a full-length 10-K.
- Whether `PragmaticSegmenterNet` is still actively maintained (last-commit/release date wasn't surfaced in this pass) — its NuGet package exists and works standalone regardless of upstream commit cadence, but longevity risk wasn't assessed.
