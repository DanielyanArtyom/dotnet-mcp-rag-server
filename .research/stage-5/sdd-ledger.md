# SDD ledger — plan: .research/stage-5/plan.md
Spec: .research/stage-5/design.md. Branch step-5. NO COMMITS (CLAUDE.md): task boundaries are index trees from `git write-tree`; review packages built with `git diff <treeA> <treeB>` via .superpowers/sdd/plan/tree-review.sh.
Start tree (HEAD 955019c): 2e1e9d23d8da89fdda33830cdbb0f1ad00a63038

## Pre-flight scan
| Pair / task | Produces → consumes | Finding |
|---|---|---|
| T1→T3 | ChunkSearchResult.ContextHeader → formatter heading | consistent |
| T2→T3,T4 | ChunkContext, GetChunkContextQuery(ChunkId,Before,After) → formatter, ChunkContextTool | consistent; DocumentChunk.ContextHeader already exists in Domain |
| T3→T4,T5,T6 | McpConstants texts/names → tools, filter, integration literals | T6 literal texts equal constants (NoResults, NotFound, Unavailable) — consistent |
| T4→T6 | tool names + camelCase params (topK, documentId, chunkId) → MCP calls in tests | consistent |
| T5→T6 | McpOptions, McpOriginMiddleware, McpToolErrorFilter.Create → wiring | consistent |
| T3↔T8 | Markdown block format → ChunkBlockParser regex | format "documentId: X  chunkId: Y  type: t\n\n<<<chunk\n…\nchunk>>>" matches regex; escape/unescape symmetric |
| T6→T9 | /api/settings mcp.contextTool → run.json (stored raw) | no code dependency |
| T8→T9 | StreamJsonParser/ChunkBlockParser/AgentTranscript → runner | consistent |
| T1 self | test overrides ContextHeaders=true over test-host pin | later UseSetting wins — ok |
| T2 self | handler validates window; tests match | ok; ValidationException ambiguity noted in plan |
| T3 self | tests ↔ code | ok |
| T4 self | tests ↔ code | ok |
| T5 self | tests ↔ code | ok |
| T6 self | Step 5 temporarily flips Stateful (obsolete warnings) then reverts | ok |
| T7 self | live check may be impossible in subagent env → syntax check fallback | ok |
| T8 self | fixture shapes to confirm against local claude | ok |
| T9 self | refactor ScoreAsync keeps full behaviour; comparer semantics per correction 8 | ok |
| T10 self | .research gitignored → only CLAUDE.md staged | ok |
| Spec vs plan | spec: --disallowedTools list, snake_case params, filter logs result count | Ruling: plan corrections 1,4,6 stand — verified against SDK source/claude --help; cost if wrong: small (doc drift only) |

Roadmap update after each task (user request): .research/roadmap.md Stage 5 section.
Task 1: minor (deferred): IntegrationTests — `using Lumen.Application.Interfaces;` now in 2 files (ContextHeaderTests + FilingDateTests) → promote to GlobalUsings (CLAUDE.md rule)
Task 1: complete (trees 2e1e9d2..0184c53, review clean)
Task 2: minor (deferred): no unit test for missing-document NotFoundException branch (GetChunkContextQuery.cs:43)
Task 2: complete (trees 0184c53..6dc4790, review clean)
Task 3: fix round 1/5 (1 addressed, 0 open — Lumen.Domain.Models promoted to Api GlobalUsings; trees 6794f47..8e5363d)
Task 3: minor (deferred): ContextHeader containing a newline would break the one-line heading (McpResultFormatter Heading) — unlikely, chunker-generated
Task 3: minor (deferred): FormatChunkContext returns "" for empty Chunks (unreachable: query throws NotFound)
Task 3: complete (trees 6dc4790..8e5363d, review clean after 1 fix round)
Task 4: fix round 1/5 (2 addressed, 0 open — Lumen.Application.Search promoted; trailing newline; trees 5bf8336..98e3f8a)
Task 4: Ruling: System.ComponentModel.DataAnnotations stays local in 3 pre-existing Api files (SearchRequest, AskRequest, DocumentsController) — promoting it makes ValidationException ambiguous (CS0104) with Lumen.Application.Exceptions; a genuine ambiguity is the documented exception to the 2+-file rule — cost if wrong: cosmetic
Task 4: minor (deferred): implementer skipped the separate RED run (tests judged sound by reviewer)
Task 4: minor (deferred): no test for get_chunk_context default window (1/1) nor whitespace-only documentId → no filter
Task 4: complete (trees 8e5363d..98e3f8a, review clean after 1 fix round)
Task 5: ⚠️ resolved by controller: McpConstants EndpointPath="/mcp" and the three error texts match the spec verbatim (Task 3).
Task 5: minor (deferred): McpToolErrorFilter.Create not unit-tested (rethrow OCE/McpProtocolException, mapping via wrapper) — Task 6 integration covers mapping
Task 5: minor (deferred): services/tenant resolution sits outside try in filter (McpToolErrorFilter.cs:90-92)
Task 5: minor (deferred): warning log for rejected calls lacks ElapsedMs
Task 5: minor (deferred): McpOptionsValidator accepts query/userinfo (http://host?x=1); no positive non-default test cases
Task 5: minor (deferred): stray double blank line McpOriginMiddlewareTests.cs
Task 5: complete (trees 98e3f8a..f8808a6, review clean)
Task 6: note: brief Step 5 as written failed at connect (SDK 2.2.0 Stateful rejects protocol 2026-07-28); with client protocol fallback the positive control failed on Assert.Contains(documentB) with Collection: [] and the negative-only scoped test PASSED under Stateful — evidence for DECISIONS (stateless = isolation requirement). Reverted; final code Stateless.
Task 6: Ruling: ModelContextProtocol.Protocol kept local (global → CS0104 Role ambiguity) — documented ambiguity exception — cost: cosmetic
Task 6: minor (deferred, FIX IN FINAL WAVE): get_chunk_context isolation test lacks a positive control (tenant A fetching its own chunk returns SharedText)
Task 6: minor (deferred): Ids(...)[0] → ArgumentOutOfRange on empty search; assert NotEmpty first
Task 6: minor (deferred): TestMcp hardcodes "/mcp" instead of McpConstants.EndpointPath (plan-mandated)
Task 6: minor (deferred): Assert.NotEqual(true, result.IsError) → clearer assertion
Task 6: complete (trees f8808a6..84d454b, review clean; full suite 390/390: unit 230, eval 77, integration 83)
Task 7: note: live login check (success / wrong password) deferred to user checklist — API not running
Task 7: minor (deferred): missing jq → bare "command not found" without lumen-token prefix
Task 7: complete (trees 84d454b..a358eeb, review clean)
Task 8: note: real stream-json shapes match the brief; claude warns "no stdin data received in 3s" unless stdin is redirected; user SessionStart hooks (plugins) emit hook events and inject context into the agent run; init model on this machine claude-opus-5
Task 8: Ruling: carry into Task 9 — (a) ToolResultText guards ValueKind==Array (null/object content → ""), (b) StreamJsonParser skips lines that fail JsonDocument.Parse (truncated output), (c) ClaudeCliAgentClient redirects+closes stdin, (d) disable user hooks in agent runs via --settings '{"disableAllHooks":true}' if verified effective, else record hooks as active in the report — why: eval contamination/robustness on the agent path — cost if wrong: small, local to harness
Task 8: minor (deferred): no tests for is_error:true, (requested) heading parse, empty tool_result content
Task 8: complete (trees a358eeb..b14b645, review clean)
Task 9: Ruling: CLI error (non-zero exit / is_error) aborts the run (exit 1, partial saved) rather than scoring wrong — fits the harness's abort-on-failure design; cost if wrong: one transient CLI error stops a long run (user re-runs)
Task 9: Ruling: "Always answer in English." added to agent Rules — user-level ~/.claude/CLAUDE.md (Russian) reaches claude -p and a translated refusal would break exact-match refusal scoring — cost if wrong: none for M1 (rules hash recorded)
Task 9: note for user: user-level CLAUDE.md/output style still reach agent runs (only hooks are disabled via --settings disableAllHooks); recorded in checklist
Task 9: fix round 1/5 (3 addressed, 0 open — CLI errors abort; agent ranking metrics null + per-category seen rate; English rule; trees 1a8d6c7..75c6d04)
Task 9: minor (deferred): QuestionsCompared non-zero for retrieval-vs-agent compare while nothing is compared; ModeMismatch warning wording for full vs agent
Task 9: minor (deferred): IsError message (transcript.Answer) not token-scrubbed (unlikely to contain it); no test for breach-before-IsError ordering
Task 9: complete (trees b14b645..75c6d04, review clean after 1 fix round)
Task 10: minor (deferred): cosmetic doc nits (quick-add token hint, jq note wording)
Task 10: complete (trees 75c6d04..736ab0b, review clean; final suite 413/413: unit 230, eval 96, integration 87; IgnoreQueryFilters 1)
Final review: 0 critical, 2 important (positive controls in get_chunk_context + scoped-search isolation tests), 9 minor; all sent in one fix wave (.superpowers/sdd/plan/final-findings.md)
Final fix wave: 11/11 addressed (re-review clean); trees 736ab0b..1899b28; suite 425/425 (unit 230, eval-harness 106, integration 89), build 0 warnings (--no-incremental), IgnoreQueryFilters 1.
Final: minor (accepted): DrainAsync 10 s cap — a grandchild holding the pipe would yield empty partial output on timeout (isolation check skipped for that question); read tasks unobserved on user cancel.
Final: Ruling: permission_denials[].tool_name field name assumed from SDK format (array confirmed present, empty in the live check) — cost if wrong: denial message lists blank names, still aborts.
