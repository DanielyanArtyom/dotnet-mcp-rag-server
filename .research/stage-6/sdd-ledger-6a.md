# SDD ledger — plan: .research/stage-6/plan-6a.md
Spec: .research/stage-6/design-6a.md. Branch step-6. NO COMMITS: task boundaries are index trees from `git write-tree`.
Start tree: 1d5767ba76b5972af751a283cb73ddeb4f8afba7

## Pre-flight scan
| Pair / task | Produces → consumes | Finding |
|---|---|---|
| T1→T4 | DatabaseOptions/DemoOptions, UseStartupDataAsync → filing step | consistent |
| T1→T5 | flag names Database__MigrateOnStartup/SeedDemoData, Demo__FilingsPath, Swagger__Enabled → compose env | consistent |
| T2↔T1 | both edit WebApplicationBuilderExtensions.Configure | disjoint lines; sequential |
| T3→T5 | /health/ready → smoke wait loop | consistent |
| T4→T5 | manifest titles → smoke expected titles | consistent (Apple 10-K/10-Q, Morgan 10-K/10-Q) |
| T5 self | NLog key Logs:FileMinLevel (correction 1) vs compose Logs__FileMinLevel | consistent |
| T1 self | test host Development keeps migrate+seed via appsettings.Development.json | ok |
| T4 self | DemoManifestTests path depth 5 to repo root | ok |
| T5 self | smoke isolated project lumen-smoke (correction 4) | ok |
| Plan vs spec | corrections 1–4 override spec (NLog key, lazy conn check, alpine volume check, smoke isolation) | Ruling: corrections stand — derived from code check; cost if wrong: small config renames |
Task 1: minor (deferred): TestWebApplicationFactory — Demo:FilingsPath pin placed under the Azure-leak comment
Task 1: minor (deferred): no test for Swagger default-off outside Development / SeedDemoData=false skip
Task 1: complete (trees start..68b63c2, review clean)
Task 2: minor (deferred): no 31/32-byte boundary or multibyte test; 32 spaces pass; whitespace-only connection string passes; JWT check duplicated (eager + ValidateOnStart, brief-mandated)
Task 2: complete (trees 68b63c2..274c6d0, review clean)
Task 3: ⚠️ resolved by controller: middleware pass-through proven by Health_endpoints_answer_anonymously going through the full pipeline (integration 99/99 green)
Task 3: minor (deferred): no explicit timeout on the ready DB check; test asserts literal "Healthy"
Task 3: complete (trees 274c6d0..26f07ae, review clean)
Task 4: Ruling: pre-validate all manifest tenants before any upload (review Important, plan-mandated shape) — spec Review Focus 3 forbids half-way ingestion — cost if wrong: one extra query pass
Task 4: fix round 1/5 dispatched (pre-resolve tenants + mixed-manifest test)
Task 4: minor (deferred): idempotency key is title only; seeder registered in AddWebApi (cosmetic)
Task 4: fix round 1/5 (1 addressed, 0 open; trees cbea3f7..a0ef0a6)
Task 4: complete (trees 26f07ae..a0ef0a6, review clean after fix)
Task 5: Ruling: Jwt__Secret uses ${JWT_SECRET:-} instead of ${JWT_SECRET:?} — the :? form broke `docker compose up -d postgres`/`down` (dev flow) without .env; the app fail-fast (Task 2) still names Jwt:Secret — cost if wrong: missing secret surfaces in api logs, not at compose config (Review Focus 5 compose half dropped)
Task 5: Ruling: plain `docker compose up` sharing the dev DB volume is documented (Task 6: dev uses `docker compose up -d postgres`); seeding skips existing titles and search filters by EmbeddingModel, so no ranking poisoning — cost if wrong: Fake vectors for a missing title in the dev DB
Task 5: fix round 1/5 dispatched (compose secret, smoke Fake providers, pipefail grep, 4-file check, .gitignore)
Task 5: env note: Docker Desktop DNS NXDOMAIN for nuget.org on this machine (workaround --add-host, not committed); integration tests need DOCKER_API_VERSION=1.43
Task 5: fix round 1/5 (5 addressed, 0 open; trees 6ac949d..a37bc44)
Task 5: minor (deferred): missing JWT_SECRET → api crash-loops while compose ps shows Up (README line in Task 6); curl|grep -q in MCP check under pipefail
Task 5: numbers: image 202 MB; cold start 7.5 s to /health/ready (migrate + 4 filings, Fake); warm restart 1.2 s; ~73 MiB; seed Apple 10-K 404 chunks 846 ms, Apple 10-Q 206/326 ms, Morgan 10-K 2596/3124 ms, Morgan 10-Q 1444/1036 ms; multi-arch amd64+arm64 OK via docker-container builder (4 min 57 s)
Task 5: complete (trees a0ef0a6..a37bc44, review clean after fix)
Task 6: Ruling: docs-only task reviewed inside the final whole-branch review (as in 6B) — cost if wrong: doc issues one step later
Task 6: known issue for final review: README roadmap labels the Azure deploy "6B" although 6B = tables (done)
Task 6: complete (trees a37bc44..dea6988, folded into final review)
Final review: ready with fixes → one fix wave (8 items: README/CLAUDE.md 6B mislabel + stale lines, empty JWT_SECRET in .env.example + random secret in smoke, MCP check pipefail, .dockerignore appsettings.Development.json, DemoManifest default FilingDate, test-host pin comment, AllowedHosts note) → re-review all addressed.
Controller: DECISIONS.md +2 entries (chiseled-extra 202 MB vs full aspnet 294 MB measured; startup migrate+seed 7.5 s cold / 1.2 s warm, Npgsql lock).
Final verification (controller): build 0 warnings; unit 305, eval 106, integration 102; IgnoreQueryFilters only UserLookupService.
