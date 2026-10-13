# SDD ledger — plan: .research/stage-2/plan.md

Spec: .research/stage-2/design.md (reachable). Branch: step-2 (in-place; .research is gitignored so no worktree).
Commit policy: CLAUDE.md forbids `git commit`. Task boundaries are `git write-tree` snapshots of the staged index (after `git add -A`), NOT commits. Review packages: `git diff-tree -p -U10 BASE_TREE HEAD_TREE`.
Integration tests: `DOCKER_API_VERSION=1.43 dotnet test ...`

## Preflight scan (2026-09-18)
| Pair / task | Produces vs consumes | Finding | Ruling |
|---|---|---|---|
| T1->T2 | DocumentChunk/ChunkType/Document fields -> DocumentChunkEntity | consistent | - |
| T2->T3 | DbSet DocumentChunks -> repo | consistent; T2/T3 tests missed Tenants FK row + NullLoggerFactory | Ruling: plan "Pre-implementation corrections" #3 governs |
| T3->existing DocumentRepositoryTests | UnitOfWork ctor gains 3rd arg | existing test breaks | Ruling: corrections #4 (update test, register IDocumentChunkRepository in DI) |
| T4/T8/T11 options binding | AddLumenServices has no IConfiguration | plan put Configure<> there | Ruling: corrections #5 (bind in Api Configure()) |
| T5->T6,T7,T8 | DocumentElement/ElementType/IDocumentReader | consistent | - |
| T7 | FileExtensionContentTypeProvider in Infrastructure | missing FrameworkReference | Ruling: corrections #6 |
| T8 | TextChunker experimental; tokenizer data pkg; line-splitting | would not build / could exceed budget | Ruling: corrections #7 |
| T9 | Content stream reuse after PdfPig | may be disposed | Ruling: corrections #8 (buffer to byte[]) |
| T9,T10->T11 | Upload/GetChunks commands are records in Commands/ Queries/ | consistent after earlier fixes | - |
| T11->T12->T13 | Upload 201 Created; seeders need RawFilePath/FilingDate | T12 fixes seeders/tests; T2 migration makes columns NOT NULL so seeders break from T2 on | Ruling: T2 implementer must also patch TestDataSeeder+DbSeeder minimally so the suite stays green per task (T12 then only migrates tests off removed endpoint) |
| T14 | GlobalUsings audit | fine | - |
| Each task self-consistency | tests vs code | T6 fixture approach changed (corrections #9); T13 fixture source (#10) | Ruling: corrections govern |

## Execution log
Task 1: complete (tree 34502bb..2f8196c, review clean; 25/25 tests)
Task 1: minor (deferred): DocumentChunkTests has redundant local usings (Lumen.Domain, Xunit already global in UnitTests) — fold into Task 14 audit
Task 2 impl done; head tree 9bf527858cf0db52c6eb2cc914c92e4996b93aa2
Task 2: complete (tree 2f8196c..9bf5278, review clean; suite green 13 unit + 13 integration)
Task 2: Ruling: accepted implementer's change Document.RawFilePath default `default!` -> `string.Empty` (stopgap so existing create endpoint doesn't insert NULL) — costs: silent empty path until Task 9/11 remove the create endpoint; revisit then
Task 2: minor (deferred): no FK/cascade-delete test; domain default revisit after Task 11
Task 3: complete (tree 9bf5278..156b408, review clean; suite 13 unit + 14 integration). Reviewer's ⚠️ (TenantId stamping on chunks) resolved by controller: AppDbContext.SaveChanges loop stamps all Added ITenantEntity, DocumentChunkEntity implements it.
Task 3: minor (deferred): no tests for chunk repo GetByIdAsync/GetAllAsync
Task 4: review (tree 156b408..39d0c93): 2 Important — path traversal via `extension` and via OpenReadAsync(relativePath). Ruling: real hole in plan-mandated code, fix in Task 4 (design: path only from ids). Minor deferred: File.Create overwrite/sync open; relative RootPath resolves against CWD (decide in Task 11 DI: resolve vs content root); happy-path-only tests.
Task 4: fix round 1/5 dispatched (resumed implementer)
Task 4: fix round 1/5 (2 addressed, 0 open; trees 39d0c93..b776790)
Task 4: complete (trees 156b408..b776790, review clean after 1 fix round; suite 20 unit + 14 integration)
Task 4: minor (deferred): regex `$` accepts trailing "\n" (use \z); no `<root>-evil` sibling test
Task 5: review (trees b776790..1f6d37a): 2 Important (walker drops text nodes/inline/li; nested-table row duplication) + minors 3 (flatten wrapper divs),4 (<br>),6 (ix:header/display:none junk). Ruling: rule minors 3,4,6 IN scope of fix round (they fall out of the walker redesign / hit every EDGAR file); cost if wrong: slightly larger fix diff.
Task 5: fix round 1/5 dispatched (resumed implementer). Deferred minors: split "$" cells; few Heading elements in EDGAR (bold spans) — design note for chunker/empirical tuning.
Task 5: fix round 1/5 (5 addressed, 0 open; trees 1f6d37a..c7bc345)
Task 5: complete (trees b776790..c7bc345, review clean after 1 fix round; suite 30 unit + 14 integration)
Task 5: minor (deferred): O(n*depth) QuerySelector(BlockSelector) per nesting level — watch in Task 13 characterization test on the real 1.5MB filing
Task 6: review (trees c7bc345..f69f2b6): 1 Important — table rows merged into one Table appended at page end (order lost; two tables merged). Fix round 1/5 dispatched (resumed implementer). Implementer deviated from brief heuristic (cell split by gap > 1.5x word height, >=3 cells = row) — accepted (reviewer: sounder than average-word-width rule; brief fragments "Net sales"). Deferred minors: one Paragraph element per prose line (chunker merges); heuristic uncalibrated on real PDFs; 2-column tables treated as prose.
Task 6: fix round 1/5 (1 addressed, 0 open; trees f69f2b6..6236dc3)
Task 6: complete (trees c7bc345..6236dc3, review clean after 1 fix round; suite 34 unit + 14 integration)
Task 7: review (trees 6236dc3..709bc9f): approved; controller build check found NU1510 (Microsoft.Extensions.Identity.Core now redundant in Infrastructure csproj due to FrameworkReference). Fix round 1/5 dispatched (remove PackageReference, add README/"" test cases). Deferred minor: null fileName -> NRE (guard in Task 9/11 if IFormFile.FileName can be null).
Task 7: fix round 1/5 (2 addressed, 0 open; trees 709bc9f..149f9b1)
Task 7: complete (trees 6236dc3..149f9b1, review clean after 1 fix round; build 0 warnings, suite 41 unit + 14 integration)
Task 8: review (trees 149f9b1..d9e6060): 3 Important — (1) table row-group split can exceed MaxTokensPerChunk (joiner tokens, single huge row) (2) empty/whitespace elements -> empty chunks (3) NU1903 Microsoft.Bcl.Memory 9.0.4 advisory via SemanticKernel.Core (security-relevant dependency; pin patched version). Ruling: also make tokenizer static + comment for /4 ratio in this round; leave usings promotion (Lumen.Application.Ingestion, 5 src files) to Task 14.
Task 8: fix round 1/5 dispatched (resumed implementer)
Task 8: fix round 1/5 (4 addressed, 0 open; trees d9e6060..de11fe9). NU1903 fixed via direct Microsoft.Bcl.Memory 10.0.12 (implementer did not look up the advisory's exact fixed version; re-reviewer judged 10.0.12 sensible — confirm `dotnet list package --vulnerable` clean in final review).
Task 8: complete (trees 149f9b1..de11fe9, review clean after 1 fix round; suite 53 unit + 14 integration)
Task 8: minor (deferred): O(n^2) joined re-count per row for oversized tables; Lumen.Application.Ingestion promotion -> Task 14; Chunker registered Scoped -> fine now that tokenizer is static
Task 9: complete (trees de11fe9..94fcc42, review clean; suite 58 unit + 14 integration). Implementer registered IDocumentReaderFactory/IChunker/IFileStorage (Scoped) in AddLumenServices (needed: 9 integration tests failed on handler resolution) — Task 11 must NOT re-register; only option binding in Api Configure() remains.
Task 9: Ruling: reader-failure->400 wrapping (corrupt HTML/PDF currently 500) and upload size limit ([RequestSizeLimit]/form limit) are carried into Task 11 dispatch (small handler edit allowed there). Known limitation (documented): orphan raw file if CompleteAsync throws (no IFileStorage.DeleteAsync).
Task 9: minor (deferred): double copy buffer.ToArray(); chunk.DocumentId set twice
Task 10: complete (trees 94fcc42..28520d4, review clean; suite 59 unit + 14 integration)
Task 10: minor (deferred): test doesn't Assert.Same on list
Task 11: review (trees 28520d4..ccbd343): 1 Important (no 403/401 tests on Admin-only upload). Ruling: also fix Minor (e) missing filingDate/title must 400. Deferred minors: DocumentDto returns full Content on upload/list (Stage 1 shape; payload size); ex.Message echoed in ValidationException on corrupt files (leaks parser text; needs ILogger); TestWebApplicationFactory temp dirs never deleted; weak ordering assertion in chunks test. Expected failures until Task 12: CreateDocumentValidationTests x3, DocumentTenantIsolationTests.A_document_created_over_HTTP_by_Tenant_A_is_invisible_to_Tenant_B.
Task 11: fix round 1/5 dispatched (resumed implementer)
Task 11: fix round 1/5 (2 addressed, 0 open; trees ccbd343..ee51a4a)
Task 11: complete (trees 28520d4..ee51a4a, review clean after 1 fix round; suite 59 unit; integration 19 pass + 4 expected failures (fixed by Task 12))
Task 12: complete (trees ee51a4a..2c23501, review clean; full suite green: 59 unit + 22 integration). Note: upload title MaxLength(500) needed a controller attribute (command record attrs are not model-bound).
Task 13: review (trees 2c23501..c7ad7ee): approved. Stats reported: Apple 10-K -> 296 chunks (209 prose/87 table), tokens min4/median201/p95236/max250, upload 0.75s, Document.Content 210,479 chars from 1.4MB HTML. Ruling: dispatch fix round for Minor "silent data loss not covered by assertions" (assert well-known phrases/table row label present) + BaseDirectory fixture path; other minors deferred (250 literal, nameof(ChunkType), None Update=).
Task 13: fix round 1/5 dispatched (resumed implementer)
Task 13: fix round 1/5 (2 addressed, 0 open; trees c7ad7ee..8787082)
Task 13: complete (trees 2c23501..8787082, review clean after 1 fix round; suite 59 unit + 24 integration, 0 warnings). Data-loss check passed; Document.Content 210k chars vs ~254k visible chars in raw HTML (chunk/content ratio 1.18 due to overlap) — unexplained ~17% gap between raw visible text and Content is a tuning topic for the user, not a defect found.
Task 14: complete (trees 8787082..db12126, review clean; suite 59 unit + 24 integration, build 0 warnings)
All 14 tasks complete. Next: final whole-branch review (HEAD 0d46198 .. staged tree db12126).
Final review (opus, trees HEAD..db12126): no Critical. 4 Important: (1) upload re-queries all chunks for Count (2) DocumentDto returns full Content in list/upload (3) 50MB cap vs ~3x in-memory (4) HTML table empty cells dropped + no header repetition on oversized-table split (retrieval-accuracy risk). Rulings: FIX NOW (one wave, resumed T11 implementer): #1 via UploadDocumentResult(Document, ChunkCount); #3 cap 50->20MB; Minor Document.RawFilePath default back to default!; stray blank line. DEFER to user/Stage 3-4: #2 (API shape/product decision — Stage 1 DTO), #4 (empirical tuning with eval set), ex.Message echo, orphan file, CharStart/End approximate (overlap), unused OpenReadAsync, section-name constants pattern, temp-dir cleanup, DELETE endpoint mentioned in design.md never existed (design.md fix by controller), NuGet vulnerability audit could not run offline (re-run `dotnet list package --vulnerable --include-transitive` online before commit).
Final fix wave dispatched (resumed implementer).
