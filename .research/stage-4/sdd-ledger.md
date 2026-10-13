# SDD ledger — plan: .research/stage-4/plan.md

Spec: .research/stage-4/design.md (read). Branch: step-4 (not main) — work in place, no worktree.
Snapshots: `.superpowers/sdd/plan/snap.sh` (private GIT_INDEX_FILE → tree id; user's index untouched, no commits).
Review packages: `.superpowers/sdd/plan/pkg.sh BASE_TREE HEAD_TREE OUT`.
Base tree (before Task 1): 2f5ae2042e0dc889dff0803cae0864b6d75429e2

## Pre-flight scan

| Pair / task | Produces → consumes | Finding |
|---|---|---|
| T1→T2 | `ChunkSearchResult.DocumentFilingDate` → `RagPrompt` filed attr | consistent |
| T2→T3 | `RagPrompt.RefusalSentence` → `FakeChatClient` | consistent (Infrastructure refs Application) |
| T2→T4 | `RagPrompt`, `CitationParser` → handler | consistent |
| T3→T4 | `IChatService`, `GeneratedAnswer` → handler | consistent |
| T3→T5 | `ChatSettingsOptions`, `ChatConstants.FakeModelName`, `ChatProvider` → controllers | consistent |
| T4→T5 | `AskQuestionQuery`, `AskAnswer`, `AnswerSource` → controller/mapping | consistent |
| T1→T3/T5 | test host forces `Chat:Provider=Fake` → ValidateOnStart | consistent |
| T5→T8 | HTTP contract → `ApiContracts`/`LumenApiClient` | auth route `api/auth/token` verified in AuthController; consistent |
| T6→T7 | `AnchorMatcher` → `RetrievalMetrics` | consistent |
| T6/T7→T8 | scoring/metrics → runner/summary/comparer | consistent |
| T8 ↔ IntegrationTests | `Lumen.Eval` (Exe, top-level statements → global `Program`) referenced by IntegrationTests that use `WebApplicationFactory<Program>` (Lumen.Api's `public partial class Program`) | RISK: name clash. Eval's generated Program is internal → likely not ambiguous; implementer must verify build |
| T1 self | tests vs code | consistent |
| T2 self | tests vs code | consistent (verified escaping/count logic by hand); `ChunkType` lives in `Lumen.Domain.Enums`, tests need that using |
| T3 self | tests vs code | consistent |
| T4 self | tests vs code | consistent |
| T5 self | tests vs code | `SettingsEndpointTests` wraps factory with second `WithWebHostBuilder` (original not disposed — minor) |
| T6 self | GlobalUsings literal vs using rule | plan's GlobalUsings lists `System.Text` (1 file in T6) and `Lumen.Eval.Scoring` (1 file outside namespace in T6) — conflicts with Global Constraint using-rule |
| T7 self | tests vs code | consistent (McNemar values checked: 6-0→0.03125, 8-1→0.0390625) |
| T8 self | tests vs code | consistent (summary/grid/comparer expectations recomputed by hand) |
| T9 self | docs | consistent |

## Rulings

- Ruling: No worktree; implement on branch `step-4` in place — user's uncommitted `appsettings.Development.json` (Azure config) must stay in effect and untouched; branch is not main — cost if wrong: none material.
- Ruling: "Snapshot" steps are run by the controller via snap.sh with a private index instead of `git add -A` on the user's index — keeps the user's staging area exactly as they left it; implementers never stage/commit — cost if wrong: user has to `git add` themselves (they commit by hand anyway).
- Ruling: Task 6 GlobalUsings — the Global Constraint using-rule wins over the plan's literal GlobalUsings listing at each task boundary (a namespace goes global only once used in 2+ files); Task 8 reconciles — cost if wrong: one extra using shuffle.
- Ruling: Task 8 `Program` clash — implementer verifies the IntegrationTests build; if ambiguous, resolve in Lumen.Eval (not Lumen.Api) with the smallest change — cost if wrong: small refactor of Eval's entry point.

## Progress
Task 1: complete (trees 2f5ae20..ccf79d3, review clean) — 94 unit + 44 integration green
Task 1: minor (deferred): filing-date test asserts only results[0] (fine with one doc)
Task 1: NOTE for user: implementer printed appsettings.Development.json diff (Azure embeddings ApiKey) into its tool output — tell user, suggest key rotation; rules hardened
Task 2: complete (trees ccf79d3..1943b2e, review clean) — 107 unit green
Task 2: minor (deferred, FIX IN FINAL WAVE): CitationParser `\d` matches Unicode digits → int.Parse FormatException → 500; use [0-9]
Task 2: minor (deferred): EscapeAttribute doesn't escape '>' (harmless); IsRefusal(null) throws (Task 4 passes non-null)
Task 3: complete (trees 1943b2e..55c3125, review clean) — 123 unit + 44 integration green; RED not observed (tests judged behaviour-asserting)
Task 3: minor (deferred → carried into Task 4 using pass): Lumen.Infrastructure.Constants used locally in 2 unit-test files → promote
Task 3: minor (deferred): ChatClientBuilder+UseLogging rebuilt per scope (plan-mandated, cheap)
Task 4: complete (trees 55c3125..7041e06, review clean) — 133 unit + 44 integration green
Task 4: minor (deferred): unknown-citation log not pinned by a test (NullLogger); vectors[0] unchecked (embedding returning empty list → 500); RetrievalMs includes embedding time (by design)
Task 5: complete (trees 7041e06..209260b, review clean) — 133 unit + 50 integration green
Task 5: Ruling (plan-sanctioned fallback): System.ComponentModel.DataAnnotations stays local in 3 Api files — promotion makes ValidationException ambiguous with Lumen.Application.Exceptions — cost if wrong: none
Task 6: review (trees 209260b..f2f241e) — Needs fixes: 1 Important
Task 6: Ruling: NumberScorer regex (plan-mandated code) lets a spaced hyphen/bullet ("- $416,161", "2024-2025") be read as a minus — fix it despite plan text; spec requires deterministic number scoring to be right, false failures would poison the E0 baseline — cost if wrong: a slightly stricter sign rule (a model writing "- 1,234" meaning negative would score wrong; parentheses/attached minus still work)
Task 6: minor (deferred): decimal.Parse overflow on huge digit runs; scale words lack \b (percentage/millionaires); expected/anchors null → NRE in loader; missing kind defaults to Number; negative tolerance not validated
Task 6: fix round 1/5 (1 addressed, 0 open; trees f2f241e..bd2a6bf)
Task 6: complete (trees 209260b..bd2a6bf, review clean after 1 fix round) — Eval.Tests 29/29
Task 7: complete (trees bd2a6bf..02a1638, review clean) — Eval.Tests 42/42
Task 7: minor (deferred): exact float compare in PairedRandomization all-zero check; iterations<=0 unguarded
Task 8: review (trees 02a1638..3c55d0c) — Needs fixes: 2 Important (+2 spec gaps)
Task 8: Ruling: spec gaps in plan-supplied code are fixed (spec is binding): (a) compare warns on different labelable-question sets (LabelableMismatch flag + console line); (b) full run fails with exit 1 when Judge.Deployment equals server chat.deployment (case-insensitive), judge deployment recorded in RunHeader and printed — cost if wrong: a user who deliberately self-judges must rename/redeploy
Task 8: Ruling: add a test for the isolation-breach abort (stub HttpMessageHandler, both modes) — core safety behaviour had no test — cost if wrong: none
Task 8: minor (deferred, consider in FINAL WAVE): Program.cs GetProperty("chat")…("deployment") before RunStore.Write can discard a finished paid run → TryGetProperty / write first; results not persisted on mid-run failure; console note column shows "unlabelable" for refusal questions; uncaught DirectoryNotFound/Json/TaskCanceled/ClientResult → stack traces; --repeat abc → 1 silently; accuracy includes unlabelable questions (defensible); cost excludes judge; same-second same-label runs overwrite; Collapse 1-1 tie = incorrect; ParseVerdict "CORRECTLY" prefix; no tests for BroadAnchors / judge-required / exit codes
Task 8: fix round 1/5 (3 addressed, 0 open; trees 3c55d0c..20db227)
Task 8: complete (trees 02a1638..20db227, review clean after 1 fix round) — Eval.Tests 60/60, Unit 133, Integration 52
Task 8: minor (deferred): judge≠answer check silently skipped if settings lack chat.deployment
Task 9: complete (trees 20db227..1c5147a, review clean) — 245/245 (133 unit + 60 eval + 52 integration)
Task 9: minor (deferred, FINAL WAVE): CLAUDE.md Stage 3 paragraph still says "Stage 4 has not started"; Stage 4a paragraph should point to the user checklist
Final review (trees 2f5ae20..1c5147a): ready after small fixes — 0 Critical, 2 Important, 9 Minor
Final: Ruling: one fix wave covers I1 (judge key out of tracked file: optional gitignored appsettings.Local.json + docs), I2 (retrieval depth: compare warns on mode/contextTopK mismatch; console hides hit@k>contextTopK on full runs), M1 (stray local using), M2 ([0-9] in CitationParser+NumberScorer), M3 (IsRefusal ignores trailing [n] markers), M4 (ParseVerdict trims markdown, requires delimiter after CORRECT), M5 (promptHash covers user template), M6 (git describe --always --dirty), M8 (warn on empty text / ContentFilter), partial-run persistence on abort, console note column for refusal rows, CLAUDE.md stale lines, checklist judge-secret + number-authoring guidance — cost if wrong: small extra surface in the harness
Final: Ruling: M3 relaxes the spec's "trimmed answer equals refusal sentence" to also allow trailing citation markers — models are told to cite every claim, strict equality would inflate false-answer rate on trap questions — cost if wrong: a refusal + citation counts as refusal (correct outcome anyway)
Final: deferred: M7 per-scope OpenAIClient/DefaultAzureCredential (revisit Stage 6; no impact with ApiKey); M9 multi-number "any match" scoring (spec-mandated; guidance goes in checklist); remaining T1–T8 minors per final triage
Final: fix wave re-review clean (trees 1c5147a..3fdc3cc); controller verified: build 0 warnings, 141 unit + 74 eval + 52 integration green, IgnoreQueryFilters=1
