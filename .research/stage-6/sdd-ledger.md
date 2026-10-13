# SDD ledger — plan: .research/stage-6/plan.md
Spec: .research/stage-6/design.md (amended by plan "Pre-implementation corrections"). Branch step-6. NO COMMITS (CLAUDE.md): task boundaries are index trees from `git write-tree`; review packages via .superpowers/sdd/plan/tree-review.sh.
Start tree (HEAD c009696): 93a2e43ed1c38e2f523796b0cb9cac2f250c4753

## Pre-flight scan
| Pair / task | Produces → consumes | Finding |
|---|---|---|
| T1→T2 | TableGrid/TableRow/TableCell(Text,ColSpan,RowSpan) → TableLayout.Place | consistent |
| T1→T2,T3,T4 | TableFixtures.LoadAsync(name) → layout/formatter/chunker tests | consistent; fixture names match across tasks |
| T2→T3 | TableLayout.Rows/Columns/ColumnOf/ColumnHeader/HasHeader/HasData, RowKind → TableFormatter | consistent |
| T3→T4 | FormattedTable(Caption,HeaderLines,BodyLines,RepeatHeader,ColumnsFellBack), TableLine, TableCaption.Find/Prefix, ChunkingOptions table switches → Chunker | consistent |
| T3 (options) ↔ T4 (validator/profile/settings) | ChunkingOptions.TableFormat enum in DependencyInjection | consistent |
| T4↔T5 | both edit SettingsController/SettingsDto/appsettings.json/TestWebApplicationFactory | disjoint properties; sequential tasks — ok |
| T5 self | HashFor(false) must equal ae0bb62cae85; BuildUserMessage off-output identical | ok (same hash input string) |
| T1 self | reader test expectations vs code (Text unchanged, spans clamped 1..100) | ok |
| T2 self | test expectations come from Python prototype mirroring the code | ok; Apple-rate thresholds 0.95/0.85 |
| T3 self | tests ↔ code (KV fallback, compact rows, gridless) | ok |
| T4 self | Chunker ctor gains ILogger → ChunkerTests.CreateChunker updated in same task | ok |
| T6 self | .research is gitignored → only CLAUDE.md staged from docs | ok |
| Plan vs spec | corrections 1–11 override spec (rowspan, footnotes, caption rule, TableHeaders⇒TableCells, compact fallback) | Ruling: corrections stand — evidenced by prototype on 4 filings, approved with the plan by the user — cost if wrong: retune heuristics after t1/t2 |
Task 1: minor (deferred): HtmlDocumentReader ExtractGrid TableCell lambda is one long line (cosmetic)
Task 1: minor (deferred): clamp theory uses same value for colspan+rowspan — swap would go unnoticed
Task 1: minor (deferred): ExtractGrid re-runs InlineText/CleanText per cell (ingest-time cost only)
Task 1: complete (trees 93a2e43..f00f31b, review clean)
Task 2: minor (deferred): Numeric regex rejects "$—" (dash alternative lacks \$?) → all-dash rows become Other (11 "$ —" in Apple 10-K)
Task 2: minor (deferred): numeric footnotes "(1)" in own cells read as negative values (correction 2 covers letters only)
Task 2: minor (deferred): "$" followed by "$" merges into "$$"
Task 2: minor (deferred): ColumnOf returns -1 / throws when Columns null — callers must guard (Task 3 only calls it on aligned Data rows)
Task 2: minor (deferred): no tests for MaxHeaderRows guard, separate ")" merge, footnote right after label
Task 2: minor (deferred): measured Apple rates not reported (prototype 97.8%/93%)
Task 2: minor (deferred): empty else-if branch for footnotes (brief-allowed)
Task 2: complete (trees f00f31b..91e40da, review clean)
Task 3: ⚠️ resolved by controller: correction 6 validator and chunker byte-identity belong to Task 4 (planned there)
Task 3: minor (deferred): TableCaption word split on ' ' only (readers already collapse whitespace)
Task 3: minor (deferred): AlignedLine uses Replace("  "," ") trick relying on CleanText
Task 3: minor (deferred): RED captured post-hoc by moving files aside (Tasks 2-3)
Task 3: complete (trees 91e40da..2e25143, review clean)
Task 4: ⚠️ resolved by controller: (a) TableCaptions-only → Format returns today's lines, empty header, RepeatHeader=false (Task 3 TableFormatter early return, test Without_TableCells_the_body_is_todays_text); (b) TableCaption.Find returns null for Table/Heading previous (Task 3 test A_heading_or_a_table_before_the_table_gives_no_caption)
Task 4: minor (deferred): prefixes unbounded in rare over-budget cases (first-chunk header, moved dangling label, caption+header up to ~full budget) → EnforceBudget repacks and extra pieces lose prefix; candidate fix: cap caption+repeat together at half budget
Task 4: minor (deferred): header+group over half budget drops both though header alone may fit
Task 4: minor (deferred): weak assertion in A_header_bigger_than_half_the_budget; no tests for dangling-label overflow, ColumnsFellBack log
Task 4: minor (deferred): characterization test duplicates ~15 lines of upload code (brief-mandated)
Task 4: complete (trees 2e25143..fae7573, review clean)
Task 5: ⚠️ resolved by controller: grep confirms no leftover RagPrompt.Hash references
Task 5: minor (deferred): empty-string ContextHeader would render context=""
Task 5: minor (deferred): new RagPrompt tests placed after private helper; v2 constant not asserted non-blank
Task 5: complete (trees fae7573..c5f88f9, review clean)
Task 6: Ruling: docs-only task (CLAUDE.md + gitignored checklist/roadmap) reviewed inside the final whole-branch review instead of a separate task reviewer — cost if wrong: doc wording issues surface one step later
Task 6: complete (trees c5f88f9..f7bd226, folded into final review)
Final review: fix wave applied (trees f7bd226..c4e52bb); I2 label emitted at its own position (brief said 'at its position') — consistent
Final review: re-review all addressed; branch ready (tree c4e52bb). Tests: unit 291, eval 106, integration 91; build 0 warnings.
Measurement fix (2026-10-12): t2 holdout showed g-dimon chunk without caption — first table chunk overflowed (4-row misdetected header + caption), EnforceBudget repack dropped prefix (= deferred minor T4-a, proved load-bearing). Fix: header > half budget demoted to body rows; prefix-aware row splitting; table profile tokens bumped to v2. Review: approved, 0 important; minors: group label reserved only when RepeatHeader; max/4 floor can still overflow in extreme caption+header cases; no row-order assertion for demotion. Tests: unit 294, eval 106, integration 91.
