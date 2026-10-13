# Hybrid search on Postgres: built-in FTS vs BM25 extensions, and RRF in one SQL statement

Sources current as of **2026-10-01**. Empirical checks were run read-only against the local `dotnet-mcp-rag-server-postgres-1` container (`pgvector/pgvector:pg17`, PostgreSQL 17.11, aarch64), whose `lumen` database holds **1,839 chunks** (JPMorgan 10-K 1,560, Apple 10-K 186, Apple 10-Q 93). No code or schema was changed; the one `CREATE TEMP TABLE` test ran in the `postgres` database and vanished with the session.

## Question

Stage 4 will probably add hybrid search (lexical + vector) and measure it on the eval set. For this corpus (SEC 10-K/10-Q text and `a | b | c` table rows, terms like "CET1 ratio", "Item 1A", "$ | 9,220"):

1. How does Postgres's built-in FTS treat this text (config, tokenizer, query parsers, ranking)?
2. Is a real BM25 extension (ParadeDB `pg_search`, Timescale `pg_textsearch`) worth it, and can it run next to pgvector on arm64?
3. How do you write RRF as one SQL statement with the tenant predicate in both branches?
4. How does the tsvector column get into an EF Core migration, and how does it fit the hand-written SQL?
5. What gain over dense-only retrieval is realistic to expect?

## Recommendation

- **Start with built-in FTS. Do not change the Docker image.** Add a **stored generated `tsvector` column** (`'english'` config) plus a **GIN index** on `DocumentChunks`. Declare it as an EF Core **shadow property** with `HasComputedColumnSql(..., stored: true)` so the domain/entity class and AutoMapper stay untouched (Finding 4).
- **Never pass a raw question to `plainto_tsquery`/`websearch_to_tsquery`.** Both AND every word together. On the real corpus, "How much did Apple spend on research and development in fiscal 2025?" matches **0 of 1,839 chunks** (Finding 1.4). Build an **OR** query from the question's own lexemes and let ranking and RRF sort things out.
- **Fuse with RRF, k = 60, in one SQL statement.** Use two CTEs, each with its own `WHERE c."TenantId" = @tenantId … ORDER BY … LIMIT @candidates`, joined with `FULL OUTER JOIN` and summed `1/(k+rank)` (Finding 3, SQL tested on the live DB). Expose per-branch weights in config so the eval loop can tune them, but start at 1.0/1.0.
- **The lexical branch is a second raw-SQL path, so the tenant-isolation test must cover it too.** Extend the existing "identical text in two tenants" test to the hybrid query. Identical text is exactly the case where the lexical branch would leak.
- **Treat a BM25 extension as a later, measured experiment, not a default.** Try it only if the eval set shows the lexical branch is the weak side. `pg_textsearch` (PostgreSQL license, PG17 arm64 binaries) is the lighter swap, but it needs a custom image plus `shared_preload_libraries`. Both BM25 options compute **corpus statistics across all tenants** in the index (Finding 2.4), which matters for a multi-tenant story.
- **Expected gain: single-digit NDCG points overall, large gains only on keyword/exact-term queries.** This range comes from vendor and lab numbers, not from our corpus (Finding 5). The eval set decides.

## Findings

### 1. Built-in FTS on this corpus

**1.1 Stored generated column vs expression index.** The Postgres docs present both options. An expression index (`CREATE INDEX … USING GIN (to_tsvector('english', body))`) must use the two-argument `to_tsvector`, "because the index contents must be unaffected by `default_text_search_config`". A separate stored column has two advantages: queries need not repeat the config, and "searches will be faster, since it will not be necessary to redo the `to_tsvector` calls to verify index matches". The expression index "is simpler to set up … and requires less disk space". [PG 17 docs §12.2.2](https://www.postgresql.org/docs/17/textsearch-tables.html)

*Measured here:* with no stored column, `to_tsvector('english', "Text") @@ 'cet1'` over 1,839 rows ran as a sequential scan in **~140 ms**, and the full hybrid query below took **~231 ms**. Almost all of that time is recomputing `to_tsvector` per row. The stored column removes that cost. Ranking (`ts_rank_cd`) also needs the tsvector of every candidate row, so a stored column helps ranking as well as matching. The docs warn that "ranking can be expensive since it requires consulting the `tsvector` of each matching document". [PG 17 docs §12.3.3](https://www.postgresql.org/docs/17/textsearch-controls.html)

**1.2 How the default parser tokenizes financial text.** This was checked with `ts_debug('english', …)` on the local container. Parser token types are documented in [PG 17 docs §12.8 / Table 12.1](https://www.postgresql.org/docs/17/textsearch-parsers.html).

| Input | Token type(s) | Lexemes (`english`) | Consequence |
|---|---|---|---|
| `$` | blank | — | Currency symbols vanish. Searching "$" is meaningless. |
| `9,220` | `uint` `9`, blank `,`, `uint` `220` | `9`, `220` | **Thousands separators split numbers.** "9,220" never exists as one lexeme. `plainto_tsquery('9,220')` → `'9' & '220'`, `phraseto_tsquery` → `'9' <-> '220'`. |
| `(1,234)` | uint, uint | `1`, `234` | Same issue; the parentheses that mark a negative number are lost. |
| `13.5%` | `float` | `13.5` | Decimals survive; `%` is dropped. |
| `2025` | `uint` | `2025` | Survives, but is very common (see 1.5). |
| `10-K` | uint `10`, blank `-`, asciiword `K` | `10`, `k` | Split. `to_tsquery('english','10-K')` → `'10' <-> 'k'` (phrase). |
| `Item 1A` | asciiword, `numword` | `item`, `1a` | `1a` is one lexeme. Good. |
| `CET1`, `FY2024`, `Q3` | `numword` | `cet1`, `fy2024`, `q3` | Survive as single lexemes. Good. |
| `taxable-equivalent` | `asciihword` + two `hword_asciipart`s | `taxable-equival`, `taxabl`, `equival` | Hyphenated words are indexed both whole and as parts. The docs say "a hyphenated word will be reported both as the entire word and as each component". [§12.8](https://www.postgresql.org/docs/17/textsearch-parsers.html) |
| `Fully`, `adjustments`, `sales` | asciiword | `fulli`, `adjust`, `sale` | Snowball stemming. |

**The number problem can be fixed in the column definition.** Strip thousands separators before tokenizing: `to_tsvector('english', regexp_replace("Text", '(\d),(?=\d{3})', '\1', 'g'))`. Tested: `9,220` → `'9220'` and `1,234,567` → `'1234567'`. It works inside a `GENERATED ALWAYS AS (…) STORED` column because `regexp_replace` is immutable (`provolatile = 'i'` in `pg_proc`). The query text must go through the **same** `regexp_replace`. This is an empirical tweak to try, not a settled choice. Whether numeric questions matter enough to justify it is an eval-set question.

**1.3 `'english'` vs `'simple'`.** The `simple` dictionary lower-cases and checks a stop-word file. Snowball stemmers "reduce common variant forms of words to a base". [PG 17 docs §12.6](https://www.postgresql.org/docs/17/textsearch-dictionaries.html) Observed locally on "The Company's CET1 ratio":
- `simple` → `'cet1' 'company' 's' 'ratio' 'the'`: no stemming, stop words kept.
- `english` → `'cet1' 'compani' 'ratio'`: stemmed, stop words removed.

Number tokenization is identical in both configs, because the parser runs before any dictionary. **Use `english`.** Questions are phrased differently from filings ("adjustment" vs "adjustments", "spend" vs "spending"), so stemming helps recall, and stop-word removal keeps OR queries from matching every chunk on "the". The stemmer's risk is over-stemming financial terms (`equival`, `fulli`). That is harmless as long as both sides are stemmed the same way.

**1.4 Query parsers: the AND trap.** [PG 17 docs §12.3.2](https://www.postgresql.org/docs/17/textsearch-controls.html)
- `to_tsquery`: the input must already be valid tsquery syntax (`&`, `|`, `!`, `<->`). It errors on free text, so it is unusable for raw questions.
- `plainto_tsquery`: free text, puts `&` between every lexeme.
- `phraseto_tsquery`: free text, puts `<->` (followed-by) between lexemes.
- `websearch_to_tsquery`: web-search syntax. Unquoted words are AND, `"quoted"` is a phrase, `or` is OR, `-x` is NOT. It never raises errors.

Measured on the real corpus:

| Question | `plainto_tsquery` output | Chunks matched (AND) |
|---|---|---|
| "How much did Apple spend on research and development in fiscal 2025?" | `'much' & 'appl' & 'spend' & 'research' & 'develop' & 'fiscal' & '2025'` | **0** |
| "What were the fully taxable-equivalent adjustments in 2025?" | `'fulli' & 'taxable-equival' & 'taxabl' & 'equival' & 'adjust' & '2025'` | 2 |
| "What was the CET1 ratio at the end of 2025?" | `'cet1' & 'ratio' & 'end' & '2025'` | 5 |
| same, as OR | `'cet1' \| 'ratio' \| 'end' \| '2025'` | 829 (45 %) |

`websearch_to_tsquery` produced the same AND query for the CET1 question. Supabase's reference hybrid function uses `websearch_to_tsquery`. That suits a search box where users type keywords, but not RAG questions. [Supabase: Hybrid search](https://supabase.com/docs/guides/ai/hybrid-search)

**Use OR semantics, built from the question's own lexemes.** Both of these forms were tested and produce a valid OR tsquery, including on input with apostrophes and hyphens:

```sql
-- (a) robust: lexemes from to_tsvector, each quoted, joined with |
array_to_string(array(SELECT quote_literal(l)
                      FROM unnest(tsvector_to_array(to_tsvector('english', @q))) l), ' | ')::tsquery
-- (b) shorter, string-replace on plainto_tsquery's text form
replace(plainto_tsquery('english', @q)::text, ' & ', ' | ')::tsquery
```

Form (a) is the one to pick. It does not depend on the text format `plainto_tsquery` happens to print, and a `::tsquery` cast of already-normalized lexemes does not re-stem them. With OR semantics, "matches" means almost nothing (45 % of chunks). All the work moves to ranking, which leads to the next point.

**1.5 Ranking: `ts_rank` vs `ts_rank_cd`, and the missing IDF.** `ts_rank` ranks "based on the frequency of their matching lexemes". `ts_rank_cd` computes "cover density", where "proximity of matching lexemes to each other is taken into consideration", and it needs positions in the tsvector, so never `strip()` it. [PG 17 docs §12.3.3](https://www.postgresql.org/docs/17/textsearch-controls.html)

The normalization bitmask:

| Flag | Effect |
|---|---|
| 0 | Ignore document length |
| 1 | Divide by 1 + log(length) |
| 2 | Divide by length |
| 4 | Divide by mean harmonic distance between extents (`ts_rank_cd` only) |
| 8 | Divide by number of unique words |
| 16 | Divide by 1 + log(unique words) |
| 32 | `rank/(rank+1)` |

Flag 32 is monotonic, so **it does not change the order and is irrelevant under RRF**, which only consumes ranks. Flags 1, 2, 8 and 16 *do* reorder, so they are worth one eval experiment because table chunks and prose chunks differ in length.

**The key weakness, quoted verbatim:** "the ranking functions do not use any global information". [§12.3.3](https://www.postgresql.org/docs/17/textsearch-controls.html) There is no IDF and this is not BM25. Measured document frequencies in this corpus: `cet1` appears in 22 chunks, `ratio` in 117, `end` in 376, `2025` in **735**. `ts_rank` weighs a hit on `2025` the same as a hit on `cet1`. A local top-6 by `ts_rank_cd` for the OR CET1 query was led by one relevant regulatory-capital note, followed by a table of share sales and a TBVPS table that matched on `2025`/`end`. That is the visible cost of having no IDF. RRF partly hides it, because only the lexical branch's *order* enters the fusion, and the vector branch votes too.

**One upside for multi-tenancy:** because built-in ranking uses no corpus-wide statistics, one tenant's documents cannot influence another tenant's lexical scores. BM25 extensions cannot claim this (see 2.4).

**1.6 Extensions in the stock `pgvector/pgvector:pg17` image.** `pg_available_extensions` lists 46 extensions. Present: `vector 0.8.6`, `pg_trgm 1.6`, `unaccent 1.1`, `fuzzystrmatch 1.2`. Absent: `pg_search`, `pg_textsearch`, `rum`, `vectorscale`. Built-in FTS and `pg_trgm` need no image change. Any BM25 extension does.

### 2. Real BM25 options

**2.1 Timescale `pg_textsearch`.**

| Aspect | Detail |
|---|---|
| What it is | "BM25 relevance-ranked full-text search" |
| Version | v1.4.0 (released 2026-08-18; v1.0.0 was the first GA release) |
| License | PostgreSQL license |
| Postgres versions | 17 and 18 |
| Builds | "Pre-built binaries … Available for Linux on amd64 and arm64". The release assets include `pg-textsearch-v1.4.0-pg17-arm64.zip`. |
| Setup | **must be in `shared_preload_libraries`**, then `CREATE EXTENSION pg_textsearch` |
| Index | `CREATE INDEX ON t USING bm25(content) WITH (text_config='english')`. Tokenization reuses Postgres text-search configs, so §1.2's number behaviour carries over. |
| Query | `ORDER BY content <@> 'terms'`, which "returns negative BM25 scores for ascending index scans". BM25 `k1` (default 1.2) and `b` (default 0.75) are configurable. |
| Limitations | No phrase queries (no positions). Partitioned tables have per-partition statistics. |
| Filtering | A plain `WHERE category_id = 1` can pre-filter via a B-tree, or else is applied as a post-filter after BM25 scoring. "Post-filtered scans automatically grow their internal scoring batch until the `LIMIT` is filled". |

Source: [pg_textsearch README](https://github.com/timescale/pg_textsearch) and [releases](https://github.com/timescale/pg_textsearch/releases).

There is no official Docker image with pgvector bundled. It would take a custom `FROM pgvector/pgvector:pg17` Dockerfile that unpacks the arm64/amd64 zip and sets `shared_preload_libraries`, and the same image in `docker-compose.yml` and in `PostgresFixture`.

**2.2 ParadeDB `pg_search`.**

| Aspect | Detail |
|---|---|
| License | Community edition is AGPL-3.0; Enterprise is commercial |
| Latest version | v0.25.11, released 2026-09-29; `0.26.0-rc.2` tagged 2026-09-30 |
| Docker image | `paradedb/paradedb`, tags for pg15–pg18 (including `pg17`, `0.25.11-pg17`), each multi-arch **amd64 + arm64** (Docker Hub tag API) |
| pgvector | **Bundled.** The `Dockerfile.paradedb-17` is `FROM postgres:17-trixie`, installs `postgresql-17-pgvector=0.8.6…`, presets `shared_preload_libraries = 'pg_search,pg_cron,pg_stat_statements'`, and its bootstrap runs `CREATE EXTENSION IF NOT EXISTS vector; … pg_search;` |
| Index syntax (current docs) | `CREATE INDEX search_idx ON mock_items USING paradedb (id, description, category) WITH (key_field='id')` |
| Scoring | `pdb.score(id)` |
| Filtering | Filters such as tenant IDs can be pushed into the index scan if the column is in the index. A `partition_by` option for "tenant IDs" segment pruning exists but is **Beta**. |
| Hybrid | Its own vector search is "a beta feature available in versions `0.25.0` and above" |

Sources: [ParadeDB README](https://github.com/paradedb/paradedb) · [Docker Hub tags](https://hub.docker.com/r/paradedb/paradedb/tags) · [Dockerfile.paradedb-17](https://github.com/paradedb/paradedb/blob/main/docker/Dockerfile.paradedb-17) · [bootstrap.sh](https://github.com/paradedb/paradedb/blob/main/docker/bootstrap.sh) · [create-index](https://www.paradedb.com/docs/reference/indexing/create-index) · [filtering overview](https://www.paradedb.com/docs/reference/filtering/overview) · [hybrid RRF](https://www.paradedb.com/docs/reference/hybrid/rrf)

Of the two, this is technically the closest drop-in swap: pgvector 0.8.6 is the same version we run, and the image covers both architectures. But it swaps the whole base image for one with Postgres-as-search-engine semantics (AGPL, a custom index type, a fast-moving 0.x API, and index syntax that has already changed from `USING bm25` to `USING paradedb`).

**2.3 Is it worth it for this project?** This is a judgement, not a sourced fact. Not as the first step. Built-in FTS plus RRF needs zero infrastructure change, keeps the "stock pgvector image" story, and creates a clean experiment ("tried built-in FTS, measured X, the no-IDF ranking failed on Y, switched to BM25, measured Z"). That before-and-after is the interview material `CLAUDE.md` asks for. Swap in BM25 only if per-query eval failures show the lexical branch ranking common-term chunks over rare-term ones. `pg_textsearch` is the less invasive choice: PostgreSQL license, stays on the pgvector base image, one extra `.so`. Its Azure Database for PostgreSQL availability (Stage 6) is not verified.

**2.4 Multi-tenant caveat for BM25 extensions.** `pg_textsearch`'s README says: "BM25 corpus statistics include all indexed rows, including rows hidden by RLS. A user who already knows a term can infer frequency information affected by inaccessible rows". [README § Limitations](https://github.com/timescale/pg_textsearch#limitations) With a shared `DocumentChunks` table, tenant B's IDF would include tenant A's chunks. That is a small information leak and also a ranking skew: JPMorgan's 1,560 chunks would dominate Apple's term statistics. The mitigations are per-tenant partial indexes or list partitioning, since "BM25 statistics are local to each partition" (same README). This mirrors the per-tenant-partitioning advice already recorded for HNSW in `stage-3/pgvector-and-dotnet.md`. Whether `pg_search` has the same global-statistics property is not documented on the pages read (see Uncertain).

### 3. Reciprocal Rank Fusion

**3.1 Origin and formula.** Cormack, Clarke and Büttcher, *Reciprocal Rank Fusion outperforms Condorcet and individual Rank Learning Methods*, SIGIR '09 (Boston, 19–23 July 2009). [PDF](https://cormack.uwaterloo.ca/cormacksigir09-rrf.pdf)

- **Formula:** `RRFscore(d) = Σ_{r∈R} 1 / (k + r(d))`, "where k = 60 was fixed during a pilot investigation and not altered during subsequent validation".
- **Why k:** "The constant k mitigates the impact of high rankings by outlier systems."
- **Sensitivity to k:** the pilot table shows k is not critical. MAP was .2072 at k=0, .2145 at k=60 and .2147 at k=80, and still .2098 at k=500. In the authors' words, "k = 60 was near-optimal, but … the choice was not critical."
- **Headline result:** RRF beat Condorcet, CombMNZ and the best single system "by 4% to 5% on average" across the TREC runs.
- **Ranks in a list are 1-based** ("each a permutation on 1..|D|"). A document missing from a list contributes 0.

**3.2 Vendor defaults and weighting.**

| Vendor | Formula / default | Weighting |
|---|---|---|
| Azure AI Search | `1/(rank + k)`; "performs best when you set k to a small value, such as 60". This k is separate from the vector query's nearest-neighbor `k`. | Vector queries can carry a weight multiplier (default 1.0) applied before fusion |
| Elastic | `rank_constant` default **60**, `rank_window_size` default 10 | Per-retriever `weight` since **9.2** ("multiplied in the RRF formula … Defaults to 1.0") |
| ParadeDB | Each branch contributes `weight / (k + rank)`; 60 is "the conventional value" | Example uses text 1.0, vector 0.7 |
| Supabase | `rrf_k` default **50** | `full_text_weight` / `semantic_weight`; uses a full outer join over two CTEs |

Sources: [Azure AI Search: Hybrid search scoring (RRF)](https://learn.microsoft.com/en-us/azure/search/hybrid-search-ranking) · [Elastic RRF retriever](https://www.elastic.co/docs/reference/elasticsearch/rest-apis/retrievers/rrf-retriever) · [ParadeDB RRF reference](https://www.paradedb.com/docs/reference/hybrid/rrf) · [Supabase hybrid search](https://supabase.com/docs/guides/ai/hybrid-search)

**Weighted RRF** is just `w_lex/(k+r_lex) + w_vec/(k+r_vec)`. Configurable weights are a cheap knob for the eval loop.

**3.3 Why RRF and not a convex combination of scores.** ParadeDB says: "BM25 scores and vector distances operate on different scales, so they cannot be added together directly … RRF … discards raw scores and uses only each row's position". [ParadeDB: How hybrid search works](https://www.paradedb.com/docs/concepts/hybrid/overview) The difference is worse here: `ts_rank_cd` values are not on any stable scale ("impossible to produce a fair normalization", PG docs §12.3.3), while cosine distance is.

The honest counterpoint: Bruch, Gai and Ingber, *An Analysis of Fusion Functions for Hybrid Retrieval*, found that a tuned **convex combination (CC) "outperforms RRF in in-domain and out-of-domain settings"**, that "RRF [is] sensitive to its parameters", and that CC "is sample efficient, requiring only a small set of training examples" with learning "generally agnostic to the choice of score normalization". [arXiv:2210.11934](https://arxiv.org/abs/2210.11934)

Read together for Lumen: RRF is the right **default** because it needs no tuning and no score normalization, and a 20–30-question eval set is small. CC (`α·norm(vec) + (1-α)·norm(lex)` with min-max normalization per query) is a legitimate **later experiment** once the eval set exists. Bruch et al. say one parameter tunes well from a few examples.

**3.4 One-statement hybrid SQL for `DocumentChunks`.** The shape below was **run read-only against the live DB** (an existing chunk's embedding served as `@query`). It returned a fused list where, for example, a chunk ranked lexical #1 / vector #4 scored 0.0320, and chunks present in only one list scored about 0.016. It took about 231 ms without a stored tsvector column. Adapted to the real schema and to `ChunkSearchRepository`'s parameters, with an assumed new column `SearchVector`:

```sql
WITH q AS (
    SELECT array_to_string(
             array(SELECT quote_literal(l)
                   FROM unnest(tsvector_to_array(to_tsvector('english', @queryText))) l),
             ' | ')::tsquery AS ts
),
lexical AS (
    SELECT c."Id",
           row_number() OVER (ORDER BY ts_rank_cd(c."SearchVector", q.ts) DESC, c."Id") AS rnk
    FROM "DocumentChunks" c, q
    WHERE c."TenantId" = @tenantId                              -- tenant predicate #1
      AND c."EmbeddingModel" = @model                           -- keep both branches over the same row set
      AND (@documentId IS NULL OR c."DocumentId" = @documentId)
      AND c."SearchVector" @@ q.ts
    ORDER BY rnk
    LIMIT @candidates                                           -- e.g. 50; NOT @topK
),
semantic AS (
    SELECT c."Id",
           row_number() OVER (ORDER BY c."Embedding" <=> @query, c."Id") AS rnk
    FROM "DocumentChunks" c
    WHERE c."TenantId" = @tenantId                              -- tenant predicate #2
      AND c."EmbeddingModel" = @model
      AND (@documentId IS NULL OR c."DocumentId" = @documentId)
    ORDER BY rnk
    LIMIT @candidates
),
fused AS (
    SELECT COALESCE(l."Id", s."Id") AS "Id",
           l.rnk AS "LexicalRank",
           s.rnk AS "SemanticRank",
           COALESCE(@wLex / (@k + l.rnk), 0) + COALESCE(@wVec / (@k + s.rnk), 0) AS "Score"
    FROM lexical l
    FULL OUTER JOIN semantic s ON s."Id" = l."Id"
)
SELECT c."Id" AS "ChunkId", c."DocumentId", d."Title" AS "DocumentTitle", c."ChunkIndex",
       c."ChunkType", c."TokenCount", c."Text",
       f."LexicalRank", f."SemanticRank", f."Score"::double precision AS "Score"
FROM fused f
JOIN "DocumentChunks" c ON c."Id" = f."Id"
JOIN "Documents" d ON d."Id" = c."DocumentId" AND d."TenantId" = @tenantId   -- the existing defence-in-depth join
ORDER BY f."Score" DESC, c."Id"
LIMIT @topK;
```

Notes on the SQL:
- **The tenant predicate must be in both CTEs, before each `ORDER BY … LIMIT`**, for the same reason as today: filtering after ranking can starve the list (see `ef-core-raw-sql-tenant-isolation.md`). The final `Documents` join keeps the Stage 3 property that the join alone is sufficient. Neither CTE may rely on it, though, because `LIMIT` has already truncated by then.
- **Use `row_number()` rather than `RANK()`.** `RANK()` (ParadeDB's choice) gives ties the same rank. That is fine too, but the `c."Id"` tie-breaker makes `row_number()` deterministic, which test assertions need.
- **Per-branch depth (`@candidates`) is a separate knob from `@topK`.** Azure's text-side default is 1,000 recall and Elastic's `rank_window_size` defaults to 10, so vendors disagree. 40–100 is a sensible starting band at about 1–2k chunks per tenant. This is an assumption to tune, not a sourced number.
- Returning `LexicalRank` and `SemanticRank` per hit costs nothing and makes eval debugging ("which branch found it?") trivial. It is the same idea as Azure's `debug` subscores. [Azure hybrid ranking](https://learn.microsoft.com/en-us/azure/search/hybrid-search-ranking)
- The `@k`, `@wLex` and `@wVec` parameters must be `numeric`/`double`, not `int`, or `1/(60+rnk)` turns into integer division (= 0). Always add a decimal (`1.0`) or type the parameter.

### 4. EF Core / Npgsql migration for the tsvector column

**4.1 Npgsql's dedicated API** ([Npgsql: Full text search](https://www.npgsql.org/efcore/mapping/full-text-search.html)):

```csharp
modelBuilder.Entity<Product>()
    .HasGeneratedTsVectorColumn(p => p.SearchVector, "english", p => new { p.Name, p.Description })
    .HasIndex(p => p.SearchVector)
    .HasMethod("GIN");
```

The docs say "any inserts or updates … will update the `SearchVector` column and maintain it automatically". The expression-index alternative is `.HasIndex(b => new { b.Title, b.Description }).HasMethod("GIN").IsTsVectorExpressionIndex("english")`.

**4.2 Limitations for Lumen.** `HasGeneratedTsVectorColumn` takes a lambda to a CLR property (an `NpgsqlTsVector` on `DocumentChunkEntity`) and only concatenates columns. It cannot express the `regexp_replace` number normalization from §1.2. Two consequences:
- A CLR property on `DocumentChunkEntity` makes `MappingProfileTests.AssertConfigurationIsValid()` fail until `DomainEntityMappingProfile`'s `DocumentChunk → DocumentChunkEntity` map ignores it. That is observed in this repo's code, not sourced.
- **Better fit: a shadow property with computed SQL.** For example, `builder.Property<NpgsqlTsVector>("SearchVector").HasComputedColumnSql("to_tsvector('english', \"Text\")", stored: true);` plus `builder.HasIndex("SearchVector").HasMethod("GIN");`. There is no entity or domain change, the column lives in the model snapshot, and future migrations will not try to drop it.

**`stored: true` is mandatory.** Npgsql's EF 10 docs say virtual generated columns (PG18) "are the default". [Npgsql: Generated properties](https://www.npgsql.org/efcore/modeling/generated-properties.html) We run PG17, which only has stored generated columns, so omitting `stored: true` would produce DDL PG17 rejects. This is inferred from the docs and not tested here.

**4.3 Raw `migrationBuilder.Sql("ALTER TABLE … ADD COLUMN … GENERATED ALWAYS AS … STORED; CREATE INDEX … USING GIN …")`** also works, because both the app (`ApplicationBuilderExtension`) and `PostgresFixture` call `MigrateAsync()`, never `EnsureCreated`. The cost: the model and snapshot don't know the column. That is invisible to EF but easy to lose in a future "recreate migrations" cleanup. Prefer 4.2's shadow property.

**4.4 Interaction with the hand-written SQL.**
- EF never writes a generated column, so the upload pipeline (`DocumentChunkRepository`/UnitOfWork) needs no change.
- Postgres fills `SearchVector` in the same `INSERT`, so the all-or-nothing upload stays intact.
- The existing chunks will get the column filled when `ALTER TABLE … ADD COLUMN … GENERATED … STORED` rewrites the table. This is standard PG behaviour for stored generated columns; at about 2k rows it is instant.
- `ChunkSearchRepository` stays an unmapped `SqlQueryRaw<T>`. The hybrid query is a second constant (or replaces the first, with a mode flag), and `@queryText` is passed as an ordinary `text` parameter.
- The isolation argument is unchanged and now covers two raw branches. The test must seed **identical text** in two tenants (identical text maximizes lexical leakage) and assert that hybrid search for tenant B never returns tenant A's rows. A negative control should run each CTE *without* its tenant predicate.

### 5. Evidence for how much hybrid helps

**5.1 Microsoft Azure AI Search team** (vendor claim, 2023-09-15). [Azure AI Search: Outperforming vector search with hybrid retrieval and reranking](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/azure-ai-search-outperforming-vector-search-with-hybrid-retrieval-and-reranking/3929167) Their setup was 512-token chunks with 25 % overlap and Ada-002 embeddings.

Table 1:

| Setup | Customer datasets (NDCG@3) | BEIR (NDCG@10) | MIRACL (NDCG@10) |
|---|---|---|---|
| Keyword (BM25) | 40.6 | 40.6 | 49.6 |
| Vector | 43.8 | 45.0 | 58.3 |
| Hybrid (RRF) | 48.4 | 48.4 | 58.8 |
| Hybrid + semantic reranker | 60.1 | 50.0 | 72.0 |

So hybrid beats vector by **+4.6 / +3.4 / +0.5** points.

Table 2 by query type (NDCG@3, keyword / vector / hybrid):

| Query type | Keyword | Vector | Hybrid |
|---|---|---|---|
| **Keyword queries** | **79.2** | **11.7** | **61.0** |
| Exact snippet search | 51.1 | 41.5 | 51.0 |
| Short queries | 53.1 | 38.8 | 53.0 |
| Fact seeking | 37.8 | 49.0 | 49.1 |
| Concept seeking | 39.0 | 45.8 | 46.3 |

Dense retrieval collapses on keyword-style queries, and hybrid recovers most of the gap (though not all of BM25's score). This is the most relevant evidence for "CET1", "Item 1A" and number lookups. Note that their keyword side is real BM25, not `ts_rank`.

**5.2 Anthropic, *Contextual Retrieval*** (vendor claim, 2024-09-19). [anthropic.com](https://www.anthropic.com/engineering/contextual-retrieval) Top-20 retrieval failure rate:

| Setup | Failure rate |
|---|---|
| Embeddings only | 5.7 % |
| Contextual embeddings | 3.7 % |
| Contextual embeddings + contextual BM25 | **2.9 %** |
| … + reranking | 1.9 % |

Adding BM25 on top of contextual embeddings cut failures from 3.7 % to 2.9 % (about a 22 % relative reduction). The post's rationale for BM25 is exact strings like the error code "TS-999", where embeddings "might retrieve general error code content". These are contextualized chunks, not plain ones, so the number does not transfer directly.

**5.3 BEIR** (Thakur et al., NeurIPS 2021 Datasets & Benchmarks), academic: "BM25 is a robust baseline", and dense models show "considerable room for improvement in their generalization capabilities" in zero-shot settings. [arXiv:2104.08663](https://arxiv.org/abs/2104.08663) This is the standard citation for why lexical retrieval still matters.

**5.4 Cormack et al. 2009:** fusion beat the best single system by about 4–5 % MAP on TREC. That is fusion in general, not specifically lexical+dense. [PDF](https://cormack.uwaterloo.ca/cormacksigir09-rrf.pdf)

**Expected range for Lumen** (my synthesis): a few points of NDCG or recall@k overall, near zero on paraphrased/conceptual questions, and **large wins on exact-term questions** (line-item names, "Item 7", "CET1", specific figures). Our lexical side is weaker than BM25 (no IDF; numbers split at commas unless normalized), so expect to land at or below the vendor gains until the lexical branch is tuned. The eval set should deliberately include a slice of exact-term and numeric questions, so this effect is visible separately rather than averaged away.

## Uncertain / could not verify

- **Whether `pg_search`'s BM25 statistics are global across tenants** like `pg_textsearch`'s. Not stated on the ParadeDB pages read (README, create-index, score, filtering, hybrid). Likely, given one index, but unverified.
- **Bruch et al.'s final venue** (commonly cited as ACM TOIS 2023). Only the arXiv abstract was read; the claims above come from the abstract alone.
- **The `HasComputedColumnSql(..., stored: false)` default → PG17 failure** is inferred from Npgsql's "virtual … are the default" wording, not tested. Also not verified: whether `HasGeneratedTsVectorColumn` itself emits `STORED` on EF 10 (it almost certainly does, since it predates PG18, but this was not checked in source).
- **`pg_textsearch` / `pg_search` on Azure Database for PostgreSQL** (a Stage 6 concern): not checked.
- **The Microsoft 2023 and Anthropic 2024 numbers** are vendor measurements on their own datasets, with real BM25 and different embedding models (Ada-002 / Voyage or Gemini). They indicate a direction, not a prediction for SEC filings with `text-embedding-3-small`.
- **Elastic's RRF page** that the first fetch returned said retrievers carry "equal weight". The newer retriever reference says `weight` exists since 9.2. The newer page is cited; the older page appears to predate 9.2.
- **The local timings (~140 ms, ~231 ms)** were single runs on a dev laptop with no stored tsvector column and no `ANALYZE`. They are indicative only. Measure again after the column exists.
- **The `@candidates` depth band (40–100)** is a judgement, not a sourced figure.
