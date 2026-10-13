# Lumen — Stage 7 design spec: polish

Status: **approved in conversation on 2026-10-12** (including the amendment that Claude records the media itself). Feeds `writing-plans` → `plan.md`.

Inputs:
- `../roadmap.md` (section "Stage 7 — Polish");
- the current `README.md` and `DECISIONS.md` (read in full on 2026-10-12);
- `../discussion/stage-6-hands-on-findings.md` (the 6B numbers);
- the conversation log `../discussion/stage-7-kickoff.md`.

## Goal and success criterion

A recruiter or interviewer who opens the repository sees, within one or two minutes:
- the product working (an MCP GIF and a Swagger screenshot near the top);
- headline numbers that match what `appsettings.json` actually runs;
- diagrams that match the code, including MCP and Docker;
- a DECISIONS.md that reads as short interview stories, each with a number.

**In scope:**
1. MCP demo GIF (Claude Code + Lumen MCP over an Azure-embedded corpus);
2. Swagger screenshot;
3. Mermaid diagrams updated and added;
4. README brought up to date with Stage 6B;
5. a medium proofreading pass over DECISIONS.md.

**Out of scope (stay deferred in the roadmap):**
- the two-replica nginx experiment;
- the `documentId: string|null` schema fix;
- renaming "Morgan" → "JPMorgan Chase" (it would break comparability with the Stage 6B runs);
- any product-code change.

## 1. Media files and their place in the README

- **Location.** `docs/media/` (new, tracked): `mcp-demo.gif`, `swagger.png`.
- **GIF placement.** Directly under the README's intro paragraphs, before "Results".
- **Screenshot placement.** In the "API" section, above the endpoint table.
- **GIF budget.** About 1000 px wide, at most ~30 s, at most 5 MB.
- **Reproducibility.** The scripts that produce both media live in `tools/media/`, so they can be re-recorded with one command each.

## 2. MCP demo GIF (Claude records it)

**Demo server.** A separate API copy, so neither the user's database nor the compose stack is touched:
- `dotnet run --project src/Lumen.Api` in Development, which loads the user's Azure user-secrets (embeddings `text-embedding-3-small`, chat `gpt-4o`);
- `ASPNETCORE_URLS=http://localhost:5219`;
- `ConnectionStrings__Default` pointing at a separate database `lumen_demo` in the same local Postgres;
- `Demo__FilingsPath=samples/edgar`, so migrations plus seeding ingest the four filings with Azure embeddings;
- cost: a one-off ~4,650 embedding calls (cents, free-trial quota).

**Client.**
- `claude` runs in a temporary directory with a `.mcp.json` whose `headersHelper` calls `tools/mcp/lumen-token.sh` with `LUMEN_URL=http://localhost:5219` and `admin@acme.test`;
- `--setting-sources project`, so the user-level `~/.claude/CLAUDE.md` (Russian replies) never reaches it — the same lesson as the M1 agent eval;
- only the `lumen` MCP tools are allowed.

**Recording.**
- VHS (`brew install vhs`), driven by `tools/media/mcp-demo.tape`;
- fallback if Claude Code's TUI renders badly under VHS: record a `claude -p` streaming session instead.

**Shots.**
1. `/mcp`: `lumen` is connected with 2 tools.
2. Acme asks *How many shareholders of record did Apple have as of October 17, 2025?*; a `search_filings` call is visible; the answer is 22,429.
3. The cross-tenant trap: Acme asks about JPMorgan's CET1 ratio. The answer is a refusal, because the other tenant's data is invisible. This is the shot that shows tenant isolation.

**Verification.**
- Claude extracts frames with `ffmpeg` and reads them as images before showing the GIF to the user;
- the user's only manual step is to accept the GIF or ask for a re-take.

## 3. Swagger screenshot (Claude takes it)

- Playwright (Chromium is already in the local cache) against the running compose API (`localhost:5119`, Fake providers), via `tools/media/swagger-screenshot.mjs`:
  1. log in via `/api/auth/token`;
  2. Authorize;
  3. the controllers' endpoint list visible;
  4. `POST /api/ask` expanded and executed ("Try it out"), the response with `sources`/`refused` in view.
- A Fake-provider response is acceptable here: the screenshot shows the API surface, not answer quality.
- If one frame can't hold both the list and the executed call, the endpoint list wins.

## 4. Mermaid diagrams

- **Ingestion (update).** A table branch:
  - `TableGrid` (spans, empty cells kept) → aligned cells with `$`/`%` merged;
  - header rows and the open row group repeated on continuation chunks;
  - the caption on every table chunk;
  - the profile example becomes the current default (`…;cells=v2;thdr=v2;tcap=v2`).
- **MCP request flow (new, sequence diagram).** Claude Code → `headersHelper` (`lumen-token.sh`) → `POST /mcp` with the JWT → Origin check → JWT bearer → `TenantResolutionMiddleware` → a stateless tool in the request's DI scope → MediatR → raw SQL with the tenant predicate → Markdown result. A note says why stateful mode would break isolation.
- **Run with Docker (new).**
  - `postgres` (pgvector, healthcheck) ← `api` (8080 → host 5119, volume `/data`, `./samples/edgar:/seed:ro`);
  - the startup sequence: migrate → seed tenants/users → ingest filings → `/health/ready`.
- **Architecture.** Checked against the code; small fixes only.
- **Syntax check.** All diagrams are rendered with `@mermaid-js/mermaid-cli` (`npx`) before they are accepted.

## 5. README brought up to Stage 6B

- **Results.** The headline is the current defaults on the extended eval set (41 questions, 35 scored), as decided:
  - t0 → t2: accuracy 77 % → 80 %, MRR 0.524 → 0.565, hit@3 52 % → 62 %, 0 % false answers, holdout 4/6;
  - the Stage 4 journey (70 % → 87 % on the original 30 questions) as a separate, clearly labelled line;
  - the text says explicitly that the two numbers come from different sets.
- **Evaluation.**
  - a second ladder table, t0…t4 (Stage 6B), next to the Stage 4 one;
  - the "known gaps" line updated: split tables are fixed; what remains is the late-header text tables, headers in a separate `<table>` and cross-filing comparisons.
- **What's interesting here.** One new bullet: the header detected from the cell grid, because EDGAR never uses `<th>`; the measurement caught the repack-without-caption bug.
- **Configuration.** New rows: `Chunking:TableCells`, `TableHeaders`, `TableCaptions`, `TableFormat`, `Chat:SourceContextHeader`.
- **Ingestion text.** The "Tables are never cut mid-row" bullet updated; the ingest-profile example updated.
- **Roadmap.** 6 ✅ (6A + 6B; the Azure deploy deliberately skipped, see "How I'd deploy to Azure"), 7 ✅.
- **Counts.** The test count and the isolation-test count are re-measured (`dotnet test`, grep), not copied from CLAUDE.md.

## 6. DECISIONS.md, medium pass

- **Keep:**
  - every entry's date, decision and numbers;
  - chronological order;
  - English only.
- **Add.** An index at the top, by stage: one line per decision with its key number, linking to the entry.
- **Fix:**
  - the duplicated "Confirmed by a control run…" sentence (hybrid and merge entries) — keep it in one entry, refer to it from the other;
  - forward references to "Stage 6" deploy / App Service → "a real Azure deploy";
  - counts that grew since (e.g. "11 isolation tests") are marked as "at the time" rather than silently updated.
- **Shorten.** The longest **Why** paragraphs are tightened to the key number plus the mechanism; no number is dropped.
- **No new entry** unless a real decision with a number comes up during this stage (no placeholders).

## 7. Closing the stage

- `CLAUDE.md`: a "Stage 7 complete" status paragraph, plus how to re-record the media.
- `.research/roadmap.md`, `.research/README.md`: Stage 7 closed.
- No commits: everything stays in the working tree for the user.

## Order of work

1. Text first: README, Mermaid, DECISIONS.
2. Then the media: the Swagger screenshot, the demo server, the GIF.
3. The user reviews the media.
4. Closing the stage.
