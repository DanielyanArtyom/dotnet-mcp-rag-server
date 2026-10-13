# SDD ledger — plan: .research/stage-7/plan.md
Setup: Ruling: no worktree, work on main without commits — CLAUDE.md forbids commits; user commits by hand and every previous stage ran in the main working tree — cost if wrong: none, nothing is committed.
Pre-flight: Task 1→2/4/5 share README section headings (Produces in Task 1); no conflicts. Task 5 consumes lumen-token.sh env (LUMEN_URL/EMAIL/PASSWORD) — matches script.
Task 4: Ruling: Swagger screenshot taken against the demo server (:5219, Development, Azure) instead of the compose API — compose api is crash-looping (no JWT_SECRET in .env) and restarting it would migrate/seed into the user's `lumen` DB, which the spec forbids touching; bonus: a real answer in the screenshot — cost if wrong: README caption differs from the plan (no "Fake" note).
Task 1: test counts re-measured: unit 305, eval 106, integration 102 = 513 (DOCKER_API_VERSION=1.43); a concurrent first run without it failed integration (env, not code). Isolation: 17 tests / 9 classes / 6 levels.
Task 1: complete (README text: Results, interesting bullet, ingestion, Stage 6B ladder, config, tests 513, roadmap; % grep reviewed, every % now labelled by set)
Task 2: Ruling: Docker diagram drawn without a startup subgraph (steps in the api node label) — the subgraph rendered as a detached box; cost if wrong: cosmetic. 7 mermaid blocks render OK (mermaid-cli 11.4.2 + Playwright Chromium). Order in MCP diagram follows Program.cs: Authentication → McpOrigin → TenantResolution → Authorization.
Task 2: complete
Task 3: Ruling: 2026-09-09 Why left as is — already dense, every clause carries a number/fact; tightening would drop facts — cost if wrong: one long paragraph remains. Removed 'Task 3'/'Task 13' references in 09-10 (internal task numbers, not facts). Number diff: no number dropped to 0; only expected decreases (dedup: 56.8/65.6/948/1,414; '13').
Task 3: complete (index 25 links resolve)
Task 4: complete (swagger.png 2560x4204, 452 KB; selector by id #operations-Ask-post_api_Ask — Ruling: Swagger route is /api/Ask (controller casing), path-text selector replaced by operation id; CSS hides curl (bearer token) and the documented example table — cost if wrong: none)
Task 5: Ruling: demo client dir is ~/lumen-demo (not scratchpad) — the scratchpad path (with the local username) shows in Claude Code's trust/welcome UI and would be published in the GIF; trust accepted once via a throwaway tape — cost if wrong: one extra folder in $HOME (remove with rm -r ~/lumen-demo)
Task 5: Ruling: GIF is 38.5 s, not ≤ ~30 s — PlaybackSpeed 1.5 plus a 6 s tail so the refusal is readable; faster playback makes typing unreadable — cost if wrong: a slightly longer loop.
Task 5: Ruling: second question phrased "Search the filings: …" — take 2 refused without calling the tool (weaker isolation story); the phrasing makes the agent search and report only Apple passages — cost if wrong: none.
Task 5: Ruling: claude launched via env -i inside the tape — the parent Claude Code session's CLAUDE_CODE_* env leaked into the banner — cost if wrong: none.
Task 5: complete (mcp-demo.gif 1000x640, 38.5 s, 762 KB; frames checked a–f; lumen DB unchanged 4 docs / 4650 chunks; lumen_demo left in place)
Task 6: complete (CLAUDE.md, roadmap, .research README, checklist; build 0 warnings; 7/7 mermaid render)
Final review: fresh reviewer (opus) — 0 critical, 3 important, 9 minor.
Final: fixed I1 question-count basis (README "35 questions = 30 + 5; retrieval over 29", CLAUDE.md) — verified eval-set.json 35 main + 6 holdout; grep for "41-question|35 scored" in README now empty.
Final: fixed I2 swagger re-take instructions (LUMEN_URL / demo server / playwright install) in README + CLAUDE.md.
Final: fixed I3 .mcp.json generation line in tape header + CLAUDE.md — sed output resolves to an executable lumen-token.sh; vhs validate OK.
Final: Ruling: re-graded minors M5 (prototype qualifier), M6 (+58 % 10-Q chunks), M7 (Task 9–13 in 09-10 Decision) to Important — wrong or inconsistent statements in interview docs; fixed by text edits; anchors still resolve — cost if wrong: none.
Final: minor (deferred): MCP diagram omits the authorization step and MediatR hop.
Final: minor (deferred): Docker diagram arrow label "starts after healthcheck" reads api→postgres.
Final: minor (deferred): demo-server.sh hard-codes postgres password/port; silently Fake without user-secrets.
Final: minor (deferred): drop-database command hard-codes the container name (docker compose exec would be portable).
Final: minor (deferred): GIF shows account banners (login expiry, Claude Pro, auto mode).
Final: minor (deferred): tape has consecutive Sleep 22s / Sleep 6s.
