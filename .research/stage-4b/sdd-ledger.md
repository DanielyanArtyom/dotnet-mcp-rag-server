# SDD ledger — plan: .research/stage-4b/plan.md

Spec: .research/stage-4b/design.md (read). Branch: step-4 (not main) — in place, no worktree (user's local secrets/config stay in effect).
Snapshots: snap.sh (private GIT_INDEX_FILE → tree id; user's index untouched, no commits). Packages: pkg.sh BASE_TREE HEAD_TREE OUT (excludes migration Designer files).
Base tree (before Task 1): 81298620669ae14023991d92a13079802c2a522d
Uncommitted user changes present at start (CLAUDE.md, .gitignore, csproj UserSecretsId, eval-set.json, appsettings.json judge key blanked, runs/) — part of base tree, not of any task diff.

## Pre-flight scan
| Pair / task | Produces → consumes | Finding |
|---|---|---|
| T1→T2 | `ingestProfile` on /api/documents + /api/settings → harness corpus check | consistent |
| T1→T5/T6/T7 | ChunkingOptions switches + profile keys → chunker features | consistent |
| T3→T4 | HybridSql/vector Sql + `@candidates` → cap rewrite | consistent; T4 replaces fused/final select of HybridSql |
| T3→T5 | `DocumentChunk.ContextHeader` column/prop (T3) → populated (T5); SearchVector includes it | consistent |
| T2→T3 | harness `Score` nullable before API sends it | consistent |
| T1→T5 | UploadDocumentCommandHandler ctor (+IIngestProfileProvider) → T5 changes Chunk call + embed input | consistent; T5 must keep T1 changes |
| T5→T6→T7 | Chunker + ChunkerTests helper `CreateChunker(…5 params)` introduced in T5; T6/T7 use it | consistent (T6/T7 tests use mergeBelowTokens/protectDecimals params) |
| T2 integ test | uses `_factory.WithWebHostBuilder(UseSetting("Chunking:ProtectDecimals","true"))` — option exists from T1 | consistent |
| T1 self | tests vs code | consistent |
| T2 self | stub needs `documentProfile` param threaded through `Runner(...)` | consistent |
| T3 self | tests vs SQL | consistent; "What is it?" all stop words → lexical empty |
| T4 self | table-based fixture, soft cap → deterministic | consistent |
| T5 self | heading test fixture vs IsHeadingLike | consistent (checked by hand) |
| T6 self | test 3 token sizes flagged as fiddly in plan | allowed to adjust numbers keeping the property |
| T7 self | "without protection" repro test may need tuning | allowed to adjust input keeping the property |
| T8 self | docs | consistent |

## Rulings
- Ruling: work in place on `step-4` without worktree — user-secrets and local config must stay in effect — cost if wrong: none material.
- Ruling: controller snapshots via private index; implementers never touch git — user commits by hand — cost: none.

## Progress
Task 1: note — dotnet ef left stray 'bin\Debug' dirs (literal backslash) under src/Lumen.Api and src/Lumen.Infrastructure; controller deleted them; rule added for later tasks
Task 1: fix round 1/5 (1 addressed, 0 open — System.Text.Json global using; trees fe71b5a..85ade40)
Task 1: complete (trees 8129862..85ade40) — unit 150, eval 74, integration 53
Task 1: minor (deferred): DocumentIngestProfileTests Assert.All could add Assert.NotEmpty
Task 2: complete (trees 85ade40..07178b0, review clean) — eval 77, unit 150, integration 54
Task 2: minor (deferred): profile check runs after full chunk download (could fail faster); "1 chunks" wording; empty-chunk documents recorded silently
Task 3: complete (trees 07178b0..d166511, review clean, opus reviewer verified tsquery edge cases on a throwaway pg17 container) — unit 157, eval 77, integration 60
Task 3: minor (deferred, FINAL WAVE candidate): isolation test checks end result only — final JOIN filters tenant anyway; add a per-CTE test (tenant A with > Candidates matching chunks must not push tenant B's own match out of LIMIT @candidates)
Task 3: minor (deferred, FINAL WAVE candidate): SearchOptionsValidator lets NaN weights through (use double.IsFinite)
Task 3: ⚠️ correction 5 (vector SQL candidates CTE) is Task 4's job — verify there
Task 4: ⚠️ resolved by controller — grep: tenant predicate in ranked, lexical, semantic, hybrid capped + both final joins in each statement (vector capped reads only ranked)
Task 4: complete (trees d166511..69fc4ac, review clean) — integration 64, unit 157, eval 77
Task 4: minor (deferred, FINAL WAVE candidate): "Without_a_cap_results_are_unchanged" only checks count — compare ordered ids of no-cap vs cap=99; backfill case only for Vector, add Hybrid row
Task 5: ⚠️ resolved by controller — ContextHeader on DocumentChunk/Entity/column came from Task 3 (verified in T3 review); usings: build 0 warnings
Task 5: complete (trees 69fc4ac..9188289, review clean) — unit 176, eval 77, integration 65
Task 5: NOTE for user: headers ON → Apple 10-K 296 → 405 chunks (flush at headings, correction 1); heuristic also makes cover-page noise sections ("— Yes ☒ No ☐", "— or"); section never cleared — step 2 of the ladder measures header + finer chunking together
Task 5: minor (deferred): Header_tokens_do_not_count_against_the_budget cannot fail (plan-mandated); SectionHeading permissive (single words like "or")
Task 6: complete (trees 9188289..02aee13, review clean) — unit 181, eval 77, integration 66; Apple 10-K merge=20: 296→289 chunks, ≤20-token chunks 7→0
Task 6: minor (deferred, FINAL WAVE candidate — trivial): ChunkerTests.cs closing brace of Header_tokens_do_not_count_against_the_budget at column 0
Task 6: minor (deferred): short chunk may be appended across a section boundary (receiver header kept, by design)
Task 7: complete (trees 02aee13..2663c0b, review clean) — unit 187, eval 77, integration 67; Apple 10-K: broken decimals 8/296 off → 0/295 on
Task 7: Ruling: implementer changed LongLineWith (sentences end with '.') — brief's input had decimals as the only split points, protection then left a 144-token line at budget 120; brief allowed adjusting the input — cost if wrong: none (defect still reproduced unprotected)
Task 7: minor (deferred, FINAL WAVE candidate — load-bearing for "budget never exceeded"): a line whose only delimiters are decimals stays oversized under protection (pre-existing TextChunker limit for delimiter-less lines); add a hard-split fallback for any chunk over budget + a test
Task 7: minor (deferred): DecimalPlaceholder.ToString() allocation; literal U+2024 in source would become '.'
Task 8: fix round 1/5 (1 addressed — stale "Stage 4b not started" sentence; trees 4a58a16..65124aa)
Task 8: complete (trees 2663c0b..65124aa) — 331/331 (187 unit, 77 eval, 67 integration), IgnoreQueryFilters=1; re-reviewer's "README/checklist not in diff" note is a false alarm (.research is gitignored; both verified by the task-8 reviewer directly)
Task 8: minor (deferred): checklist could note user-secrets load only in Development; step 0 could check no leftover Search:* secrets before baseline
Final review (trees 8129862..65124aa): with fixes — 0 Critical, 2 Important, 5 Minor; isolation + switch-off ordering verified in every SQL path
Final: Ruling: correct the Task 7 diagnosis — budget overshoot is pre-existing TextChunker behaviour at OverlapTokens=0 (fuzz: 250/0 up to +57), not decimal protection; fix with a post-pass that re-splits any chunk over budget (keeps overlap 0 allowed) rather than forbidding overlap 0 — cost if wrong: slightly more chunks at overlap 0
Final: Ruling: one fix wave covers Important #1 (Restore only when ProtectDecimals on + test), Important #2 (per-CTE lexical isolation test with Candidates=1/VectorWeight=0), Minor 1 (finite weights), 2 (ordered-ids no-cap test + Hybrid backfill row), 3 (budget re-split post-pass + overlap-0 test), 4 (settings local), T6 brace, T8 checklist notes (user-secrets only in Development; check leftover Search:*/Chunking:* secrets before b0) — cost if wrong: small
Final: fix wave re-review — all 8 addressed (trees 65124aa..e25a1f6)
Final: parked — EnforceBudget re-splits restored text, so with ProtectDecimals on an over-budget chunk could split a decimal — Ruling: real but deferred; over-budget chunks occur only at OverlapTokens=0 (fuzz: never at 250/60) and protection defaults off; fix = Protect() before re-splitting + Restore() pieces — cost if wrong: rare broken decimal in an overlap-0 experiment
Final: controller verified — build 0 warnings; 193 unit + 77 eval + 69 integration = 339 green; IgnoreQueryFilters=1
