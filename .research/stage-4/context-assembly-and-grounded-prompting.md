# Context assembly and grounded prompting for `POST /api/ask`

Sources current as of **2026-10-01**. This covers the generation half of Stage 4: how retrieved chunks become a prompt, how the model is told to cite and refuse, and what the endpoint returns. Retrieval (hybrid/FTS/RRF) and eval methodology are separate topics.

## Question

Stage 4 adds single-turn, non-streaming `POST /api/ask` over SEC 10-K/10-Q filings: retrieve top-k chunks (prose ~250 tokens with 60-token overlap; tables rendered as `a | b | c` rows, split by rows without repeating the header; known defects: `$ | 9,220` split into two cells, lost colspan so year headers misalign) → assemble context → a mini-class Azure OpenAI chat model writes an answer citing chunks, and refuses when the context doesn't contain the answer. Each tenant only sees its own chunks; Stage 5 exposes retrieval as MCP tools. Six questions:

1. Context assembly: block format and metadata, order and lost-in-the-middle, deduplicating overlapping chunks, merging/expanding adjacent chunks, token budget (cl100k vs o200k).
2. Grounding and citation prompting: system prompt pattern, inline `[n]` vs structured JSON, checking citations after the fact, what the GPT-4.1/GPT-5 guides say.
3. Prompt injection from untrusted filing text: delimiting and instructions, at a level that fits this project.
4. Tables in context: which serialization LLMs read best, re-attaching headers, numeric pitfalls.
5. Refusals: making them reliable, the false-refusal trade-off, whether a similarity threshold should skip the LLM call.
6. API response shape, compared with what similar systems return.

## What the code has today (relevant facts)

- `ChunkSearchResult` (`src/Lumen.Domain/Models/ChunkSearchResult.cs`) carries `ChunkId, DocumentId, DocumentTitle, ChunkIndex, ChunkType (Prose|Table), TokenCount, Text, Distance`. `SearchChunksQueryHandler` embeds the query, then `ChunkSearchRepository` runs the tenant-scoped raw SQL. `/api/ask` can reuse this unchanged.
- `Document` has only `Title` and `FilingDate`. **There is no filing type (10-K/10-Q), company or fiscal period field.** Any `type="10-K" period="FY2025"` attribute in a source block has to come from the title or from new columns.
- The `Chunker` (`src/Lumen.Infrastructure/Chunking/Chunker.cs`) takes prose overlap from `TextChunker.SplitPlainTextParagraphs(..., overlapTokens)`. **Table chunks never overlap.** `SplitTableIfOversized` cuts by rows and emits continuation chunks **with no header row**. The chunk records **no table identity**: you can't tell from a chunk which table it belongs to, or whether two adjacent table chunks are one table or two. `CharStart/CharEnd` are flagged in the code as approximate, so offsets can't be used to deduplicate overlap.
- `HtmlDocumentReader.ExtractTableText` **drops empty cells** (`.Where(text => text.Length > 0)`) before joining with `" | "`. Together with lost colspan, this is the mechanism behind the column misalignment seen in the JPM table, not only the `$` cell.
- The tokenizer is `TiktokenTokenizer.CreateForModel("gpt-4")`, i.e. cl100k, from `Microsoft.ML.Tokenizers` 2.0.0 + `.Data.Cl100kBase`.

## Recommendation

1. **Source block format:** use XML-style tags. Give each source a short integer `id` (1..n) that the server maps back to `ChunkId`. Include metadata attributes (`title`, `filed`, `kind="prose|table"`). Don't use JSON. Order sources by relevance, best first. Put the question **after** the sources, and repeat the key rules (answer only from sources, cite, refuse) both in the system message and right after the sources. Both OpenAI and Anthropic document this pattern (Findings 1–3).
2. **Assembly pipeline, done in code before the LLM call:**
   (a) Merge chunks from the same document whose `ChunkIndex` values are consecutive into one block. For prose, remove the duplicated overlap by matching the suffix of one chunk against the prefix of the next; offsets are approximate, so don't use them.
   (b) Don't expand to neighbouring chunks by default; make it an experiment knob.
   (c) Fill a token budget greedily in rank order, and never truncate a chunk in the middle.
   (d) Count tokens with the **chat model's** encoding (o200k for every current GPT-4o/4.1/5.x model), not cl100k. That means adding `Microsoft.ML.Tokenizers.Data.O200kBase`. With 250-token chunks and topK ≤ 10, a context budget of ~3,000 tokens is a safety net rather than a binding constraint. Read the real numbers from `ChatResponse.Usage`.
3. **Citations:** have the model write plain text with inline `[n]` markers. The server parses them, drops any `n` outside 1..n, and builds the citation list from its own id→chunk map. The model never sees or emits GUIDs. Flag answers that cite nothing but don't refuse. As a cheap financial-specific check, verify that every number in the answer appears in at least one cited chunk; that one is my own suggestion, not a vendor recommendation. Keep strict JSON Structured Outputs as an E6 comparison, not the default (Finding 6).
4. **Refusal:** instruct the model to reply with an **exact sentinel sentence** when the sources don't answer the question, so the server and the eval harness can detect it by string match. **Don't** put a cosine-distance threshold in front of the LLM by default. Skip the LLM only when retrieval returns zero chunks (the tenant has no documents). Log the top-1 distance on every request so E6 can show whether answerable and unanswerable questions actually separate (Finding 12).
5. **Prompt injection, at the right scale:** put the sources in the **user** message, not the system/developer message. Wrap them in tags, and say once that their content is data and never instructions. Escape any literal closing tag inside chunk text. That's enough for this threat model: only an `Admin` uploads documents, only to their own tenant, and `/api/ask` has no tools. Spotlighting by datamarking or base64 is overkill here (Finding 9).
6. **Tables:** the evidence on serialization format is mixed and depends on the model (Finding 10). The defects affect results more than the format choice. Fix them **at ingest**, not at context time: repeat the header row(s) on table continuation chunks, keep empty cells (or emit placeholders) so columns line up, and glue a standalone `$` or `%` onto the neighbouring number. A context-time workaround isn't reliable, because chunks don't record which table they belong to. Optionally render as a real markdown table with a `|---|` separator line. Add prompt rules for units ("in millions"), parenthesised negatives and fiscal-year columns.
7. **Response:** return `answer`, `refused` (bool), `citations[]` with `{ sourceId, chunkId, documentId, documentTitle, chunkIndex, chunkType, snippet, distance }`, an optional `sources[]` listing everything that was sent with a `cited` flag (like On Your Data's `all_retrieved_documents` / `UNCITED_REFERENCE`), `usage` (`inputTokens`, `outputTokens`, `totalTokens`), `model`, and `timings` in ms (`retrieval`, `generation`, `total`). Separate timings are what the eval harness needs to back the "generation dominates latency" hypothesis.
8. **Model-choice side note for E5:** on Azure, reasoning models (`gpt-5-mini`, `gpt-5.4-mini`, …) **reject `temperature` and `top_p`**. A temperature/top-p experiment is only possible on a non-reasoning mini such as `gpt-4.1-mini`, or by tuning `reasoning_effort` instead (Finding 7). MEAI's `ChatOptions.TopK` exists, but OpenAI chat APIs have no `top_k`. The project's real "top-k" is the retrieval topK.

## Findings

### 1. Context format: XML or ID/TITLE/CONTENT; avoid JSON

- The OpenAI GPT-4.1 prompting guide reports that in its long-context tests "XML performed well", for example `<doc id='1' title='The Fox'>…</doc>`. The format `ID: 1 | TITLE: The Fox | CONTENT: …` "also performed well", and "JSON performed particularly poorly". Markdown headings are suggested as a starting point. Source: [GPT-4.1 Prompting Guide](https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide).
- Anthropic recommends wrapping each document in `<document>` with `<source>` and `<document_content>` subtags (and other metadata). Source: [Claude prompting best practices — long context](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices).
- The OpenAI Model Spec treats XML/JSON/YAML-quoted content as untrusted data with no authority (Finding 9). The tags therefore serve two purposes: they give the model structure, and they mark the content as data.
- The pipe-separated ID/TITLE/CONTENT format clashes with this project's `a | b | c` table rows. That's another reason to prefer XML tags here (my inference).

Suggested block:

```
<sources>
<source id="3" title="JPMorgan Chase 10-Q" filed="2026-08-01" kind="table">
… chunk text …
</source>
</sources>
```

### 2. Ordering and "lost in the middle"

- Liu et al., *Lost in the Middle* (TACL 2023): "performance is often highest when relevant information occurs at the beginning or end of the input context, and significantly degrades when models must access relevant information in the middle". With 20 documents, GPT-3.5-Turbo scored **75.8%** with the answer at position 1, **53.8%** with it in the middle, and **56.1%** closed-book. In the middle position it did worse than with no documents at all. Source: [arXiv 2307.03172](https://arxiv.org/abs/2307.03172), [HTML](https://arxiv.org/html/2307.03172).
- In the same paper, going past 20 retrieved documents improved the reader only slightly (~1.5% for GPT-3.5-Turbo, ~1% for claude-1.3) and lengthened the context a lot. "Query-aware contextualization" (repeating the query before and after the documents) "minimally affects performance trends in the multi-document question answering task". [HTML](https://arxiv.org/html/2307.03172).
- LlamaIndex ships `LongContextReorder`, which moves the strongest nodes to both ends of the context to address this effect. [LlamaIndex node postprocessors](https://developers.llamaindex.ai/python/framework/module_guides/querying/node_postprocessors/node_postprocessors/).
- Anthropic's Contextual Retrieval study found that passing the **top 20** chunks worked better than the top 10 or top 5, across its datasets. [Anthropic — Contextual Retrieval](https://www.anthropic.com/news/contextual-retrieval).
- What follows for us: rank order (best first) is the simple default that the evidence supports. The paper's models were from 2023, and current models are better at long context, but how much better is unverified. Reordering and topK (3/5/10/20) are exactly the E4 lever, so measure them rather than assume.

### 3. Where instructions go relative to the documents

- GPT-4.1 guide: "place your instructions at both the beginning and end of the provided context, as we found this to perform better than only above or below". If the instructions appear only once, "above the provided context works better than below". [GPT-4.1 Prompting Guide](https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide).
- Anthropic: "Put longform data at the top … above your query". "Queries at the end can improve response quality by up to 30 percent in tests, especially with complex, multidocument inputs." [Claude prompting best practices](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices).
- Microsoft (On Your Data): "If there is a certain behavior that is critical to the application, you can repeat the behavior in system message to increase its accuracy." [Use your data (classic)](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/concepts/use-your-data).
- Resulting layout: system message with the rules → user message containing `<sources>…</sources>`, then a short reminder of the rules, then the question last.

### 4. Deduplication, adjacent merging and expansion

- Microsoft's chunking guidance treats overlap as a deliberate duplication to preserve continuity, "without excessive duplication", with 10–25% as starting points. Our 60/250 is 24%. [Azure AI Search — Chunk documents](https://learn.microsoft.com/en-us/azure/search/vector-search-how-to-chunk-documents). If two consecutive chunks are both retrieved, ~60 tokens of the budget are spent twice and the model sees the same sentences twice. Merging them into one block, overlap removed, fixes that. The merge rule needs only `DocumentId` + consecutive `ChunkIndex` + `ChunkType == Prose`, all of which `ChunkSearchResult` already has. The overlap itself is found by text matching (the longest suffix of chunk *i* that equals a prefix of chunk *i+1*), because `CharStart/End` are approximate.
- Expanding to neighbours: LlamaIndex's `PrevNextNodePostprocessor` "fetches adjacent nodes … when a specific node matches a query", and sentence-window retrieval swaps the matched node for a wider window. [LlamaIndex node postprocessors](https://developers.llamaindex.ai/python/framework/module_guides/querying/node_postprocessors/node_postprocessors/). This is common practice, but I found no vendor evidence that it helps in general. Treat it as an experiment, not a default. The obvious use case here is a table continuation chunk whose header sits in the previous chunk (Finding 10).

### 5. Token budget and tokenizer

- Microsoft's sizing rule: "the chunk size multiplied by the number of retrieved documents is the total number of tokens sent to the model". On Your Data reserves 20% of the window for the response and gives what's left after the system/meta prompt to chunks. [Use your data (classic)](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/concepts/use-your-data).
- Every current OpenAI chat model uses **o200k_base**: `gpt-4o-*`, `gpt-4.1-*`, `gpt-5-*`, `gpt-5.1…5.6-*`, `gpt-6-*`, `o1/o3/o4-mini`. That's the model→encoding map in `TiktokenTokenizer.cs` on the `dotnet/machinelearning` main branch: [source](https://github.com/dotnet/machinelearning/blob/main/src/Microsoft.ML.Tokenizers/Model/TiktokenTokenizer.cs). The data package `Microsoft.ML.Tokenizers.Data.O200kBase` is published at 2.0.0 stable: [NuGet index](https://api.nuget.org/v3-flatcontainer/microsoft.ml.tokenizers.data.o200kbase/index.json). Chunk `TokenCount` values stored with cl100k are approximate for budgeting a GPT-4.1/5 prompt; how far off they are for this corpus is unmeasured.
- Budget enforcement: walk the ranked (merged) blocks and add whole blocks while `systemTokens + blockTokens + questionTokens + reservedOutput ≤ budget`. Stop at the first block that doesn't fit, or skip it and try smaller ones; that's a design choice. Never cut a chunk mid-text: a half table row is worse than no row. The authoritative count is `ChatResponse.Usage` from MEAI; local counting only decides what to include. (Design inference; the API surface is from [ChatOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatoptions).)

### 6. Grounding and citation prompt patterns

- Wording from the GPT-4.1 guide for "context only" mode: "Only use the documents in the provided External Context to answer the User Query. If you don't know the answer based on this context, you must respond 'I don't have the information needed to answer that.'" Its citation instruction: "always include citations immediately after the relevant statement(s)", using a fixed citation format. [GPT-4.1 Prompting Guide](https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide).
- Microsoft's suggested system-message text: "Please answer using retrieved documents only, and without using your knowledge. Please generate citations to retrieved documents for every claim in your answer. If the user question cannot be answered using retrieved documents, please explain the reasoning … In any case, don't answer using your own knowledge." On Your Data's own answers use `[doc1]`-style inline markers, which the client swaps for links using `context.citations[i]`. [Use your data (classic)](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/concepts/use-your-data), [On Your Data API reference](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/references/on-your-data).
- GPT-5 guide: "poorly-constructed prompts containing contradictory or vague instructions can be more damaging to GPT-5 than to other models, as it expends reasoning tokens searching for a way to reconcile the contradictions". XML-like spec sections improved instruction adherence, and "minimal reasoning performance can vary more drastically depending on prompt". [GPT-5 Prompting Guide](https://developers.openai.com/cookbook/examples/gpt-5/gpt-5_prompting_guide). Practical consequence: keep the rule set short and free of conflicts. Don't write "be helpful and complete" next to "refuse if unsure" without saying which one wins.
- Anthropic says prompt-based quoting/citation is weaker than its API-level Citations feature, which "guaranteed to contain valid pointers to the provided documents" and is "significantly more likely to cite the most relevant quotes … than purely prompt-based approaches". It also accepts RAG chunks as "custom content" documents. [Claude Citations](https://platform.claude.com/docs/en/build-with-claude/citations). Azure OpenAI has no equivalent, so our citations are prompt-based and **must be validated server-side**. OWASP likewise advises: "request detailed reasoning and source citations, and use deterministic code to validate adherence". [OWASP LLM01:2025](https://genai.owasp.org/llmrisk/llm01-prompt-injection/).
- Inline markers vs Structured Outputs:
  - Structured Outputs guarantees the response matches the schema (JSON mode only guarantees valid JSON). It's supported on gpt-4o-mini and later, and safety refusals come back in a separate `refusal` field. [OpenAI Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs).
  - Tam et al. 2024, *Let Me Speak Freely?*: "a significant decline in LLMs reasoning abilities under format restrictions … stricter format constraints generally lead to greater performance degradation in reasoning tasks". [arXiv 2408.02442](https://arxiv.org/abs/2408.02442).
  - Trade-off: inline `[n]` in free text keeps answer quality and is trivial to parse with `\[(\d+)\]`. JSON (`{answer, citedSourceIds, answerable}`) makes the refusal detectable deterministically. The sentinel string recovers most of that benefit without the schema.

### 7. Sampling parameters on current Azure models (affects E5)

- Azure reasoning-models page: "Reasoning models other than GPT-6 Astra don't support the following parameters: `temperature`, `top_p`, `presence_penalty`, `frequency_penalty`, `logprobs`, `top_logprobs`, `logit_bias`, `max_tokens`". They require `max_completion_tokens` on Chat Completions. `reasoning_effort` supports `none` on gpt-5.1…5.6, and `minimal` only on the original GPT-5 models. [Azure OpenAI reasoning models](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning). The table on that page lists `gpt-5-mini` and `gpt-5.4-mini` under the reasoning models.
- MEAI `ChatOptions` does expose `Temperature`, `TopP`, **`TopK`**, `MaxOutputTokens`, `Reasoning`, `ResponseFormat` and `Seed`. [ChatOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatoptions). `TopK` has no OpenAI counterpart (the OpenAI chat API has `top_p` but no `top_k`). That confirms the open question in `stage-4-kickoff.md`.

### 8. Similar systems' response shapes

- **Azure OpenAI On Your Data** (deprecated, **retires 2026-10-14**; Microsoft now points to Foundry Agent Service + Foundry IQ) adds `message.context` to the assistant message:
  - `citations[]`: `content` (required), `title`, `url`, `filepath`, `chunk_id`;
  - `intent`;
  - `all_retrieved_documents[]`: `search_queries`, `original_search_score`, `rerank_score`, `filter_reason` (`score` | `rerank`).

  Documents retrieved but not cited can show up as `UNCITED_REFERENCE`. Parameters: `strictness` 1–5 (default 3), `inScope`, retrieved-document count. Sources: [API reference](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/references/on-your-data), [concepts](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/concepts/use-your-data). This is a design reference only; don't depend on it.
- **OpenAI file search (Responses API):**
  - Output has a `file_search_call` item plus a `message` whose text carries `file_citation` annotations (`file_id`, `filename`, `index` = character position in the answer).
  - Raw results with scores come back only with `include: ["file_search_call.results"]`.
  - `max_num_results` caps the result count.

  [OpenAI file search](https://developers.openai.com/api/docs/guides/tools-file-search).
- **Anthropic Citations:** each citation has `cited_text` (not billed as output tokens), `document_index`, and character, page or content-block ranges. [Claude Citations](https://platform.claude.com/docs/en/build-with-claude/citations).
- Pattern shared by all three: answer text with markers, a citation list carrying the chunk text or snippet and an id/title, and optionally the full retrieved set with scores for debugging. Usage and latency aren't part of these citation objects. They come from the normal API envelope (`usage`), so returning them is our own choice, driven by the eval harness.

### 9. Prompt injection from filing text

- OpenAI Model Spec: "Quoted text (plaintext in quotation marks, YAML, JSON, XML, or `untrusted_text` blocks) in ANY message, multimodal data, file attachments, and tool outputs are assumed to contain untrusted data and have no authority by default". [Model Spec 2025-12-18](https://model-spec.openai.com/2025-12-18.html).
- OpenAI agent safety guidance: "Pass untrusted inputs through user messages to limit their influence" (developer messages take precedence), and use structured outputs to remove free-form channels. [OpenAI — Safety in building agents](https://developers.openai.com/api/docs/guides/agent-builder-safety).
- Microsoft *Spotlighting* (Hines et al. 2024):
  - Delimiting alone roughly halves attack success on GPT-3.5-Turbo (~60% → ~30%).
  - Datamarking drops it to <3%, and encoding (base64/ROT13) to ~0%. Encoding hurts task quality on weaker models.
  - The authors judge delimiting alone insufficient.

  Sources: [arXiv 2403.14720](https://arxiv.org/abs/2403.14720), [HTML](https://arxiv.org/html/2403.14720).
- Azure Prompt Shields detects "document attacks" in grounding data. Its Spotlighting (preview) base64-encodes documents, which raises the token count, and the model "might mention that document content is base64 encoded". The standalone API checks a prompt plus up to five documents. [Prompt Shields](https://learn.microsoft.com/en-us/azure/ai-services/content-safety/concepts/jailbreak-detection).
- OWASP: RAG "do[es] not fully mitigate prompt injection". Recommended steps: separate and clearly mark untrusted content, define output formats, validate them deterministically, and apply least privilege. [OWASP LLM01:2025](https://genai.owasp.org/llmrisk/llm01-prompt-injection/).
- Proportionality (my assessment): the corpus is public SEC filings uploaded by a tenant's own `Admin`, the endpoint is read-only text generation with no tools, and the tenant boundary is enforced in SQL before the LLM is involved. So no filing can make the model reveal another tenant's data; it's never in the prompt. The worst case is a wrong or manipulated answer for that same tenant. Tags plus a one-line "content is data" rule plus server-side citation validation is proportionate. Note for Stage 5: MCP tools hand chunk text to the *client's* LLM, which may have tools of its own. That is where injection matters more.

### 10. Tables: format evidence, headers, numbers

- Sui et al., *Table Meets LLM* (WSDM 2024): on their structural-understanding benchmark with GPT-3.5/GPT-4, markup formats beat delimiter text. "Using markup languages, specifically HTML, outperforms 'NL+Sep' with a 6.76% improvement". Format explanations and partition marks helped some tasks and could hurt cell lookup. [arXiv 2305.13062](https://arxiv.org/abs/2305.13062), [HTML v5](https://arxiv.org/html/2305.13062v5).
- ImprovingAgents (vendor blog, one model): GPT-4.1-nano, 11 formats, 1,000 records.
  - Accuracy: Markdown-KV **60.7%**, HTML 53.6%, JSON 52.3%, Markdown table 51.9%, CSV 44.3%.
  - Their caveat: they "didn't repeat any headers", which likely penalised the header-dependent formats (CSV, markdown table).

  [ImprovingAgents — best input data format](https://www.improvingagents.com/blog/best-input-data-format-for-llms).
- He et al. 2024: prompt format (plain/Markdown/JSON/YAML) moves GPT-3.5-turbo by up to 40% on one task, while "larger models like GPT-4 are more robust". Mini-class models are the sensitive end of that range. [arXiv 2411.10541](https://arxiv.org/abs/2411.10541).
- Taken together, no format wins universally. Every result depends on the header being visible to the model for each row it reads. Our continuation chunks have no header, and our rows have shifted columns (dropped empty cells, a standalone `$`). So fixing the data at ingest matters more than choosing between pipe, markdown and HTML. HTML would cost tokens for a gain measured on GPT-3.5/4 structural tasks, not on QA with current minis.
- Header re-attachment belongs at ingest. The chunker already knows the table's first row(s) when it splits, and the embedding of a continuation chunk also benefits from having the header. Doing it at context time would mean guessing table membership from `ChunkIndex` adjacency, and that guess fails when two tables sit next to each other. Changing it at ingest requires re-chunking and re-embedding the 4 filings. (Design inference.)
- Microsoft also suggests prepending document context to chunks: "append the document title to chunks from the middle of the document to prevent context loss". [Chunk documents](https://learn.microsoft.com/en-us/azure/search/vector-search-how-to-chunk-documents). Anthropic's Contextual Retrieval does the same with an LLM-written prefix; its own example is an SEC-filing chunk. Retrieval failures fell 35% (49% with BM25, 67% with reranking). [Anthropic — Contextual Retrieval](https://www.anthropic.com/news/contextual-retrieval).
- Numeric difficulty:
  - FinanceBench: GPT-4-Turbo with a shared vector-store retrieval "incorrectly answered or refused to answer 81% of questions". [arXiv 2311.11944](https://arxiv.org/abs/2311.11944).
  - TAT-QA (hybrid table and text financial QA): numerical reasoning is "usually required", and the best model was at 58.0 F1 vs 90.8 for human experts. [arXiv 2105.07624](https://arxiv.org/abs/2105.07624).

  Prompt rules worth adding (my synthesis; no single vendor source):
  - Keep the units given in the source ("in millions", "in thousands", %), and state them in the answer.
  - `(1,234)` means negative.
  - Identify the column's fiscal period and say which period a number belongs to.
  - 10-Q columns are often three- *and* nine-month periods.
  - Don't compute figures that aren't asked for. When computing, show the operands with their citations.

### 11. Refusals and their trade-off

- Joren et al., *Sufficient Context* (2024/25): large models (GPT-4o, Gemini 1.5 Pro, Claude 3.5) "excel at answering queries when the context is sufficient, but often output incorrect answers instead of abstaining when the context is not". Smaller models "hallucinate or abstain often, even with sufficient context". Guided selective abstention improved accuracy-when-answering by 2–10%. [arXiv 2411.06037](https://arxiv.org/abs/2411.06037). Expect both failure directions from a mini model. That's why the eval set needs unanswerable questions **and** answerable-but-hard ones (tables), so false refusals get counted too.
- A fixed sentinel ("I don't have the information needed to answer that." in the GPT-4.1 guide) gives an exact-match signal. On Your Data's alternative is to have the model explain why the documents don't answer, which gives a better user experience but is harder to score automatically. [GPT-4.1 guide](https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide), [Use your data](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/concepts/use-your-data). Allowing "sentinel + one sentence on what is missing" combines both, if the harness checks for a prefix rather than an exact match.

### 12. Similarity threshold before the LLM

- Azure AI Search: vector search "always return[s] `k` results, even if the nearest neighbors aren't that similar". A minimum threshold is an option "but only if the query is a pure single vector query. Hybrid queries aren't conducive to minimum thresholds because the RRF ranges are much smaller and more volatile." The `threshold` parameter itself is still **preview**. [Create a vector query](https://learn.microsoft.com/en-us/azure/search/vector-search-how-to-query). Azure also rescales cosine into `1/(1+distance)`, so its scores aren't raw cosine. [Vector ranking](https://learn.microsoft.com/en-us/azure/search/vector-search-ranking).
- On Your Data's `strictness` is exactly this kind of filter, tuned by trial: "If you find that the chatbot omits relevant information, lower the filter's strictness". [Use your data](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/concepts/use-your-data).
- OpenAI documents no meaningful score ranges or thresholds for its embedding models; it only recommends cosine and notes the vectors are unit-length. [OpenAI embeddings guide](https://developers.openai.com/api/docs/guides/embeddings). Community reports say `text-embedding-ada-002` cosine scores cluster in roughly 0.7–1.0, so thresholds have to be recalibrated per model. This is a secondary source, not official: [OpenAI community thread](https://community.openai.com/t/why-cosine-similarity-between-embedding-vectors-is-always-above-68/661144).
- What follows for us: a distance threshold is model-specific (it would have to be re-tuned after any `Embeddings:Provider`/deployment change, the same failure mode the `EmbeddingModel` column guards against), and it stops working once hybrid/RRF lands in E2. Log distances and decide from E6 data. The only safe short-circuit is "no chunks at all".

## Uncertain / could not verify

- **Whether lost-in-the-middle still matters for 2025–26 mini models at our scale** (5–10 blocks, ≤3k tokens). The paper's numbers are for 2023 models with 10–30 documents. No current primary source quantifies it for gpt-4.1-mini or gpt-5-mini. Measure in E4.
- **Table format for current minis:** Sui et al. tested GPT-3.5/4 on structural tasks, and ImprovingAgents tested only GPT-4.1-nano on synthetic records with no repeated headers. Neither tested financial-statement QA. I found no study comparing pipe rows against markdown tables for SEC tables.
- **Structured Outputs vs free text for RAG answer quality:** the Tam et al. degradation result is about reasoning tasks and has been publicly disputed (not followed up here). Its relevance to short grounded answers is unverified.
- **How much cl100k counts deviate from o200k on this corpus.** Not measured; it's a one-line check once the o200k package is added.
- **Exact current model availability and pricing** of `gpt-4.1-mini` vs `gpt-5-mini`/`gpt-5.4-mini` in the user's Azure region. I didn't check this; it decides whether E5 (temperature) is runnable at all.
- **The full GPT-5 / GPT-5.x guides may contain RAG-specific citation advice** that the fetched summary didn't surface. I only saw the instruction-adherence, XML-spec and reasoning-effort points.
- **OpenAI file search `ranking_options.score_threshold` semantics.** Mentioned on the page, but the details weren't retrieved.
- The "numbers in the answer must appear in a cited chunk" check and the "sentinel + reason" refusal format are my own proposals, not sourced recommendations.
