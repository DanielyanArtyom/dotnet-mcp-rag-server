# Stage 6B: eval-set candidates (questions about continuation chunks of JPM tables)

Written on 2026-10-12, before any system answer, from the chunks of the current corpus (profile `chunk=250/60;hdr=v1;merge=0;dec=v1`). The values were checked against the source HTML (`samples/edgar/jpm-20260630.html`, `jpm-20251231.html`).

Anchors were checked with the harness's normalization: lowercase, dashes and quotes unified, `|` replaced by a space, a match when every substring of an anchor list is present. Each anchor matches 1–2 chunks in tenant globex, below the "broad anchor" threshold of 5.

**Why these questions.** In every one, the answer row sits in a **continuation chunk** of a long table, and that chunk lacks the column header (5 quarters + 6 months, or 3 years). The JPM 10-Q "Consolidated financial highlights" table is cut into chunks 25–33. The JPM 10-K highlights table and the CCB segment table are cut too. Today the model sees a row like `Net charge-off rate | 0.66 | % | 0.67 | % | …` without knowing which column is 2Q26.

| id | question | answer | where the answer is (current corpus) | prose duplicate? |
|---|---|---|---|---|
| `g-nco-rate-q2-26` | What was JPMorgan Chase's net charge-off rate for the second quarter of 2026? | 0.66 % | 10-Q #33: a 53-token chunk, one row, no header | no. Only the table has it (the 0.10 % in the wholesale table is a trap) |
| `g-employees-q2-26` | How many employees did JPMorgan Chase have at June 30, 2026? | 320,560 | 10-Q #32: continuation, first row of the chunk | no |
| `g-slr-q2-26` | What was JPMorgan Chase's supplementary leverage ratio at June 30, 2026? | 5.5 % | 10-Q #30: continuation, first row | yes. 10-Q #56: "SLR was 5.5%." |
| `g-alll-ratio-2025` | What was JPMorgan Chase's allowance for loan losses to total retained loans ratio at December 31, 2025? | 1.83 % | 10-K #320: continuation | yes. 10-K #347: "coverage ratio of 1.83%" |
| `g-ccb-employees-2025` | How many employees did JPMorgan Chase's Consumer & Community Banking segment have at December 31, 2025? | 144,196 | 10-K #469: continuation of the CCB table, row `Employees \| 144,196 \| (c) \| 144,989 …` | the employee table (10-K #71, `CCB \| 144,196`), also a table |

Questions 1, 2 and 5 depend only on the tables. Questions 3 and 4 have a prose duplicate: the comparison will show whether the system finds the table row or answers from the prose.

All five are `table-value`, `holdout: false`. Adding them changes the eval-set hash. t0 becomes the new baseline.

## JSON for `tools/Lumen.Eval/eval-set.json`

```json
{ "id": "g-nco-rate-q2-26", "tenant": "globex", "category": "table-value",
  "question": "What was JPMorgan Chase's net charge-off rate for the second quarter of 2026?",
  "expected": { "kind": "number", "value": 0.66, "unit": "percent" },
  "anchors": [["Net charge-off rate", "0.66", "0.67", "0.72"]],
  "holdout": false, "note": "10-Q Consolidated financial highlights, continuation chunk (Stage 6B)" },
{ "id": "g-employees-q2-26", "tenant": "globex", "category": "table-value",
  "question": "How many employees did JPMorgan Chase have at June 30, 2026?",
  "expected": { "kind": "number", "value": 320560 },
  "anchors": [["Employees", "320,560"]],
  "holdout": false, "note": "10-Q Consolidated financial highlights, continuation chunk (Stage 6B)" },
{ "id": "g-slr-q2-26", "tenant": "globex", "category": "table-value",
  "question": "What was JPMorgan Chase's supplementary leverage ratio at June 30, 2026?",
  "expected": { "kind": "number", "value": 5.5, "unit": "percent" },
  "anchors": [["Supplementary leverage ratio", "5.5", "5.6", "5.8"], ["SLR was 5.5%"]],
  "holdout": false, "note": "10-Q highlights continuation chunk + Executive Overview prose (Stage 6B)" },
{ "id": "g-alll-ratio-2025", "tenant": "globex", "category": "table-value",
  "question": "What was JPMorgan Chase's allowance for loan losses to total retained loans ratio at December 31, 2025?",
  "expected": { "kind": "number", "value": 1.83, "unit": "percent" },
  "anchors": [["Allowance for loan losses to total retained loans", "1.83", "1.87"], ["coverage ratio of 1.83%"]],
  "holdout": false, "note": "10-K highlights continuation chunk + credit prose (Stage 6B)" },
{ "id": "g-ccb-employees-2025", "tenant": "globex", "category": "table-value",
  "question": "How many employees did JPMorgan Chase's Consumer & Community Banking segment have at December 31, 2025?",
  "expected": { "kind": "number", "value": 144196 },
  "anchors": [["Employees", "144,196", "144,989"], ["CCB", "144,196"]],
  "holdout": false, "note": "10-K CCB selected metrics continuation chunk + employee breakdown table (Stage 6B)" }
```
