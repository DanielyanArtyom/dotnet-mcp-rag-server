# Stage 6B — Split tables keep their header, caption and columns: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Table chunks carry their column headers, row-group labels and caption, with `$`/`%` merged into numbers and empty cells kept in position. Each change sits behind its own config switch so the eval harness can measure it. `/api/ask` can optionally show the model each chunk's context header.

**Architecture:**
1. `HtmlDocumentReader` keeps each table's cell grid on `DocumentElement.Table` and leaves `Text` unchanged.
2. A pure `TableLayout` turns the grid into placed, merged cells: row kinds (header / data / group label / other) and logical columns.
3. `TableFormatter` renders those rows as pipe or key-value lines. `TableCaption` finds the caption in the preceding paragraph.
4. `Chunker` packs the lines into chunks within the token budget and prefixes continuations.
5. A `Chat:SourceContextHeader` switch adds a `context` attribute to `/api/ask`'s `<source>` tags.

**Tech Stack:** .NET 10, AngleSharp, Microsoft.ML.Tokenizers (cl100k), xUnit, Testcontainers (integration), MediatR.

**Spec:** `.research/stage-6/design.md`. Research: `.research/stage-6/table-splitting-headers-and-captions.md`. Read the "Pre-implementation corrections" below first; they override the spec where they differ.

## Global Constraints

- **Never run `git commit` or `git push`.** Leave every change in the working tree. The user commits by hand. (CLAUDE.md overrides the skill's "Commit" steps; each task ends with "Stage" instead.)
- **With every new switch at its default, chunk text, `Document.Content` and the ingest profile are byte-identical to today's.**
- All new switches default to off / `Pipe` in code and in `src/Lumen.Api/appsettings.json`.
- **Ingest profile tokens** are appended only when non-default: `;cells=v1`, `;thdr=v1`, `;tcap=v1`, `;tser=kv1`. With all of them off, the profile stays `chunk=250/60;hdr=v1;merge=0;dec=v1` (with today's appsettings).
- **Integration test host** pins `Chunking:TableCells=false`, `Chunking:TableHeaders=false`, `Chunking:TableCaptions=false`, `Chunking:TableFormat=Pipe`, `Chat:SourceContextHeader=false`.
- `RagPrompt.HashFor(false)` must equal today's hash, **`ae0bb62cae85`** (from `tools/Lumen.Eval/runs/20261007-094501_final-full/run.json`).
- C# conventions (CLAUDE.md):
  - no narrating comments;
  - `GlobalUsings.cs` rules: promote a namespace used in 2+ files of a project, keep the four blocks `Lumen.*` → `Microsoft.*` → `System.*` → third-party, leave no unused usings;
  - DI-constructed classes use a classic constructor plus `private readonly` fields;
  - Options classes and their enums live in `DependencyInjection/`;
  - DTOs only in `Lumen.Api/DTO`.
- Build has 0 warnings. `IgnoreQueryFilters` stays only in `UserLookupService`.
- Unit tests: `dotnet test tests/Lumen.UnitTests`. Integration tests need Docker: `dotnet test tests/Lumen.IntegrationTests`.

## Pre-implementation corrections (override the spec)

Found while prototyping the algorithm in Python on all four sample filings (`samples/edgar/`). The prototype is not part of the repo.

1. **`rowspan` is supported**, not ignored. Cells are placed on an occupancy grid, the HTML table algorithm: a cell with `rowspan=n` blocks its columns in the next `n-1` rows. The JPM filings have 164/201 `rowspan` attributes, and without them header rows shift left (e.g. `2Q26` landing above the label column). The reader stores `RowSpan`. Spans are clamped to `1..100`.
2. **Footnote-marker cells** (`(a)` … `(zz)`, any cell after the first that matches `^\([a-z]{1,2}\)$`) are dropped. They don't merge into the neighbouring value and don't extend its span. JPM puts `(e)` in a cell of its own between columns, and treating it as a value made 7 % of JPM tables ambiguous.
3. **Data row** = first cell is a *label* (non-numeric, **or a year** such as `2026` in a maturity schedule) and some later cell is a *value* (numeric but **not a bare year**). Without the year rules, year header rows counted as data and year-labelled schedules counted as all-header.
4. **Header block** = the leading rows before the first data row, **minus trailing single-cell rows that start inside the label column**. Those trailing rows are row-group labels (`ASSETS:`, `Current assets:`). The guard is **≤ 6 rows** (not 4): JPM's average-balance tables have a 3-row title above a 2-row header. The token guard (≤ half the budget) applies when a continuation prefix is built.
5. **Caption** = the **sentence** of the preceding paragraph that contains `following table` / `as follows` / `summar` (case-insensitive; the last such sentence), or the paragraph's last sentence if it ends with `:`. It is **cut to 40 words** plus ` …`, and the paragraph's own length doesn't matter. Reason: the `g-dimon-10b5-1` caption paragraph is **167 words**, so the spec's "≤ 60 words" rule would have dropped the one caption the step exists for.
6. **`TableHeaders` requires `TableCells`**, enforced by the validator just like `KeyValue`. Header rows are found on the normalized grid. The "A′ on today's lines" combination (spec §2.6, row 2) existed only for completeness and is removed. `TableCaptions` alone is still allowed: it uses today's table text plus the caption.
7. **Tables whose columns can't be aligned** (`Columns == null`) render under `TableCells` as **compact pipe**: merged non-empty cells, no empty slots. Today's text is not used for them. They're counted (`ColumnsFellBack`) and logged at Debug. Measured on the samples: 1/46 Apple 10-K, 0/29 Apple 10-Q, 5/424 JPM 10-K, 4/235 JPM 10-Q tables with data rows.
8. **Column header labels** skip *title rows* (a header row with one cell starting inside the label column) and header cells starting inside the label column. Each part is trimmed of a trailing `,`/`:`/space, and parts are joined top-down with `, `. Known limitation: some JPM header rows have spans that don't line up with their data (e.g. `Six months ended June 30,` spanning the 3-month `Change` column), so that column's key-value label inherits the wrong period. This is not worth a heuristic; the measurement shows whether it matters.
9. **Fallback-rate test:** a unit test runs `HtmlDocumentReader` + `TableLayout` over the Apple 10-K fixture. The fixture is linked from `tests/Lumen.IntegrationTests/Fixtures/apple-10k-fy2025.htm`, which is byte-identical to the sample. It asserts aligned ≥ 95 % of tables with data rows (prototype: 97.8 %) and a detected header in ≥ 85 % of aligned tables (prototype: 93 %).
10. **Known gap, recorded and not built:** 143 aligned JPM 10-K tables have **no header row of their own** (their first row is already data; the header lives in a separate `<table>` above). That's option E, out of scope. A′ and C can't help those tables. Mention it in the hands-on log if t2/t3 disappoint on JPM 10-K.
11. A dangling **row-group label at the end of a chunk** moves to the start of the next chunk. It's free once lines are typed, and it is the exact `g-cet1-q2-26` shape (`Capital ratios - Standardized(a)` ending chunk 47).

## Review Focus

1. **Hostile or odd span attributes** (`colspan="0"`, `"-3"`, `"abc"`, `"99999"`, a `rowspan` past the last row): spans clamp to `1..100`, there's no huge allocation, and the table still renders. Test in Task 1 and Task 2.
2. **A table with no data rows** (exhibit list, signature block, trading-arrangements table) with every switch on: compact pipe lines, no header, no `ColumnsFellBack`, no exception. Test in Task 3.
3. **A header block that alone exceeds half the budget** (small `MaxTokensPerChunk` with the JPM 3-row title header): continuations go without the header, and every chunk stays `TokenCount ≤ MaxTokensPerChunk`. Test in Task 4.
4. **A caption paragraph that is huge or has no sentence punctuation**: the caption is ≤ 40 words + ` …`, and a non-matching paragraph or a heading gives no caption. Test in Task 3.
5. **A PDF table element (`Table == null`) with every switch on**: today's text (plus caption if `TableCaptions`), no exception, and the decimal guard still applies. Test in Task 4.

---

## File map

| File | Status | Responsibility |
|---|---|---|
| `src/Lumen.Application/Ingestion/TableGrid.cs`, `TableRow.cs`, `TableCell.cs` | create | raw grid contract (reader → chunker) |
| `src/Lumen.Application/Ingestion/DocumentElement.cs` | modify | `TableGrid? Table` |
| `src/Lumen.Infrastructure/DocumentReading/HtmlDocumentReader.cs` | modify | fill `Table`; `Text` unchanged |
| `src/Lumen.Infrastructure/Chunking/RowKind.cs`, `LayoutCell.cs`, `LayoutRow.cs`, `LayoutColumn.cs`, `TableLayout.cs` | create | placement, merge, row kinds, columns, column headers |
| `src/Lumen.Infrastructure/Chunking/TableLine.cs`, `FormattedTable.cs`, `TableFormatter.cs`, `TableCaption.cs` | create | rendering and caption rule |
| `src/Lumen.Infrastructure/Chunking/Chunker.cs` | modify | formatted-table path, previous-element tracking, prefixes |
| `src/Lumen.Infrastructure/DependencyInjection/TableFormat.cs` | create | `Pipe` / `KeyValue` |
| `src/Lumen.Infrastructure/DependencyInjection/ChunkingOptions.cs`, `ChunkingOptionsValidator.cs` | modify | four switches + rules |
| `src/Lumen.Infrastructure/Chunking/IngestProfileProvider.cs` | modify | conditional tokens |
| `src/Lumen.Infrastructure/DependencyInjection/ChatSettingsOptions.cs` | modify | `SourceContextHeader` |
| `src/Lumen.Application/Constants/PromptConstants.cs`, `Ask/RagPrompt.cs`, `Ask/AskQuestionQuery.cs` | modify | `context` attribute, `HashFor` |
| `src/Lumen.Api/Controllers/AskController.cs`, `SettingsController.cs`, `DTO/Responses/SettingsDto.cs`, `appsettings.json` | modify | wiring + settings output |
| `tests/Lumen.UnitTests/Fixtures/Tables/*.html` | create | 8 real table fixtures |
| `tests/Lumen.UnitTests/Lumen.UnitTests.csproj` | modify | copy fixtures, link Apple 10-K |
| `tests/Lumen.UnitTests/DocumentReading/HtmlDocumentReaderTests.cs` | modify | grid tests |
| `tests/Lumen.UnitTests/Chunking/TableLayoutTests.cs`, `TableFormatterTests.cs`, `TableCaptionTests.cs`, `TableFixtures.cs` | create | unit tests + fixture loader |
| `tests/Lumen.UnitTests/Chunking/ChunkerTests.cs`, `ChunkingOptionsValidatorTests.cs`, `IngestProfileProviderTests.cs` | modify | |
| `tests/Lumen.UnitTests/Ask/RagPromptTests.cs`, `AskQuestionQueryHandlerTests.cs` | modify | |
| `tests/Lumen.IntegrationTests/Utils/TestWebApplicationFactory.cs`, `Settings/SettingsEndpointTests.cs`, `Documents/DocumentIngestionCharacterizationTests.cs` | modify | pins, settings, Apple 10-K with switches on |

---

### Task 1: Table grid on `DocumentElement` + reader fills it (+ test fixtures)

**Files:**
- Create: `src/Lumen.Application/Ingestion/TableGrid.cs`, `TableRow.cs`, `TableCell.cs`
- Modify: `src/Lumen.Application/Ingestion/DocumentElement.cs`, `src/Lumen.Infrastructure/DocumentReading/HtmlDocumentReader.cs`
- Create: `tests/Lumen.UnitTests/Fixtures/Tables/*.html` (8 files), `tests/Lumen.UnitTests/Chunking/TableFixtures.cs`
- Modify: `tests/Lumen.UnitTests/Lumen.UnitTests.csproj`, `tests/Lumen.UnitTests/DocumentReading/HtmlDocumentReaderTests.cs`

**Interfaces:**
- Produces: `TableGrid { List<TableRow> Rows }`, `TableRow { List<TableCell> Cells }`, `TableCell { string Text; int ColSpan = 1; int RowSpan = 1 }` (namespace `Lumen.Application.Ingestion`); `DocumentElement.Table` (`TableGrid?`); `HtmlDocumentReader.MaxSpan = 100`; test helper `TableFixtures.LoadAsync(string name) : Task<DocumentElement>` (the single table element of a fixture file).

- [ ] **Step 1: Create the fixtures.** Save this script as `<scratchpad>/extract_table.py` (outside the repo) and run it with system `python3` (lxml is installed in the user site). It cuts one table out of a sample filing, keeping only `colspan`/`rowspan` and cell text:

```python
"""Usage: extract_table.py <filing.html> <tableIndex> <maxRows> <out.html>"""
import sys, re, html
from lxml import html as LH
src, idx, max_rows, out = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
doc = LH.parse(src).getroot()
table = list(doc.iter('table'))[idx]
lines = ['<html><head><meta charset="utf-8"></head><body>', '<table>']
rows = [tr for tr in table.iter('tr') if next(tr.iterancestors('table')) is table][:max_rows]
for tr in rows:
    cells = []
    for td in tr:
        if td.tag not in ('td', 'th'):
            continue
        attrs = ''.join(f' {a}="{td.get(a)}"' for a in ('colspan', 'rowspan') if td.get(a) not in (None, '1'))
        text = html.escape(re.sub(r'\s+', ' ', td.text_content()).strip(), quote=False)
        cells.append(f'<{td.tag}{attrs}>{text}</{td.tag}>')
    lines.append('  <tr>' + ''.join(cells) + '</tr>')
lines.append('</table>')
lines.append('</body></html>')
open(out, 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
```

```bash
S=samples/edgar; F=tests/Lumen.UnitTests/Fixtures/Tables; mkdir -p $F; X=<scratchpad>/extract_table.py
python3 $X $S/aapl-20250927.html 18 40  $F/apple-operating-expenses.html
python3 $X $S/aapl-20250927.html 17 40  $F/apple-gross-margin-percentage.html
python3 $X $S/aapl-20250927.html 24 16  $F/apple-balance-sheet.html
python3 $X $S/jpm-20260630.html  15 8   $F/jpm-revenue.html
python3 $X $S/jpm-20260630.html  8  12  $F/jpm-selected-data.html
python3 $X $S/jpm-20260630.html  11 200 $F/jpm-financial-performance.html
python3 $X $S/jpm-20260630.html  257 200 $F/jpm-capital-ratios.html
python3 $X $S/jpm-20251231.html  43 5   $F/jpm-trading-arrangements.html
```

Check: `apple-operating-expenses.html` row 2 contains `<td colspan="3">2025</td>`, and `jpm-selected-data.html` contains `rowspan=` and a `<td>(e)</td>` cell. The table indices are lxml document order, verified 2026-10-11.

- [ ] **Step 2: csproj.** Add to `tests/Lumen.UnitTests/Lumen.UnitTests.csproj`:

```xml
  <ItemGroup>
    <None Update="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
    <None Include="..\Lumen.IntegrationTests\Fixtures\apple-10k-fy2025.htm" Link="Fixtures\apple-10k-fy2025.htm" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing reader tests** (append to `HtmlDocumentReaderTests.cs`):

```csharp
    [Fact]
    public async Task ReadAsync_keeps_the_table_grid_with_spans_and_empty_cells()
    {
        const string html = """
            <html><body><table>
              <tr><td></td><td colspan="3">2025</td></tr>
              <tr><td rowspan="2">Products</td><td>$</td><td>112,887</td><td></td></tr>
              <tr><td>%</td></tr>
            </table></body></html>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var table = Assert.Single(await new HtmlDocumentReader().ReadAsync(stream));

        Assert.NotNull(table.Table);
        Assert.Equal(3, table.Table!.Rows.Count);
        Assert.Equal(["", "2025"], table.Table.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal(3, table.Table.Rows[0].Cells[1].ColSpan);
        Assert.Equal(2, table.Table.Rows[1].Cells[0].RowSpan);
        Assert.Equal(4, table.Table.Rows[1].Cells.Count);
    }

    [Fact]
    public async Task ReadAsync_leaves_the_table_text_unchanged()
    {
        const string html = "<html><body><table><tr><td></td><td colspan=\"3\">2025</td></tr><tr><td>Products</td><td>$</td><td>112,887</td><td></td></tr></table></body></html>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var table = Assert.Single(await new HtmlDocumentReader().ReadAsync(stream));

        Assert.Equal("2025\nProducts | $ | 112,887", table.Text);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-3", 1)]
    [InlineData("abc", 1)]
    [InlineData("99999", HtmlDocumentReader.MaxSpan)]
    [InlineData("4", 4)]
    public async Task ReadAsync_clamps_span_attributes(string attribute, int expected)
    {
        var html = $"<html><body><table><tr><td colspan=\"{attribute}\" rowspan=\"{attribute}\">A</td><td>1</td></tr></table></body></html>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var cell = Assert.Single(await new HtmlDocumentReader().ReadAsync(stream)).Table!.Rows[0].Cells[0];

        Assert.Equal(expected, cell.ColSpan);
        Assert.Equal(expected, cell.RowSpan);
    }

    [Fact]
    public async Task ReadAsync_does_not_take_rows_of_a_nested_table_into_the_outer_grid()
    {
        const string html = "<html><body><table><tr><td>Outer</td><td><table><tr><td>Inner</td></tr></table></td></tr></table></body></html>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var outer = (await new HtmlDocumentReader().ReadAsync(stream))[0];

        Assert.Single(outer.Table!.Rows);
    }
```

- [ ] **Step 4: Run them to verify they fail.** Run: `dotnet test tests/Lumen.UnitTests --filter FullyQualifiedName~HtmlDocumentReaderTests`. Expected: compile errors (`Table`, `MaxSpan` don't exist).

- [ ] **Step 5: Implement.** `TableGrid.cs`:

```csharp
namespace Lumen.Application.Ingestion;

public class TableGrid
{
    public List<TableRow> Rows { get; set; } = [];
}
```

`TableRow.cs`:

```csharp
namespace Lumen.Application.Ingestion;

public class TableRow
{
    public List<TableCell> Cells { get; set; } = [];
}
```

`TableCell.cs`:

```csharp
namespace Lumen.Application.Ingestion;

public class TableCell
{
    public string Text { get; set; } = string.Empty;
    public int ColSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
}
```

`DocumentElement.cs` gets `public TableGrid? Table { get; set; }`.

In `HtmlDocumentReader`, add `public const int MaxSpan = 100;`. Extract the row selection into `OwnRows` so both methods share it. Fill the grid:

```csharp
        if (tag == "table")
        {
            var tableText = ExtractTableText(element);
            if (!string.IsNullOrWhiteSpace(tableText))
                elements.Add(new DocumentElement { Type = ElementType.Table, Text = tableText, Table = ExtractGrid(element) });
        }
```

```csharp
    private static IEnumerable<IElement> OwnRows(IElement table) =>
        table.QuerySelectorAll("tr").Where(r => r.Closest("table") == table);

    private static IEnumerable<IElement> CellsOf(IElement row) =>
        row.Children.Where(c => TagOf(c) is "td" or "th");

    private static string ExtractTableText(IElement table)
    {
        var lines = new List<string>();
        foreach (var row in OwnRows(table))
        {
            var cells = CellsOf(row)
                .Select(cell => CleanText(InlineText(cell)))
                .Where(text => text.Length > 0);
            var line = string.Join(" | ", cells);
            if (line.Length > 0)
                lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    private static TableGrid ExtractGrid(IElement table) => new()
    {
        Rows = OwnRows(table)
            .Select(row => new TableRow
            {
                Cells = CellsOf(row)
                    .Select(cell => new TableCell { Text = CleanText(InlineText(cell)), ColSpan = SpanOf(cell, "colspan"), RowSpan = SpanOf(cell, "rowspan") })
                    .ToList()
            })
            .ToList()
    };

    private static int SpanOf(IElement cell, string attribute) =>
        int.TryParse(cell.GetAttribute(attribute), out var span) && span > 0 ? Math.Min(span, MaxSpan) : 1;
```

`PdfDocumentReader` is untouched (`Table` stays null).

- [ ] **Step 6: Fixture loader** `tests/Lumen.UnitTests/Chunking/TableFixtures.cs`:

```csharp
namespace Lumen.UnitTests.Chunking;

public static class TableFixtures
{
    public static async Task<DocumentElement> LoadAsync(string name)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tables", name + ".html"));
        return (await new HtmlDocumentReader().ReadAsync(stream)).Single(e => e.Type == ElementType.Table);
    }
}
```

- [ ] **Step 7: Run all unit tests.** Run: `dotnet test tests/Lumen.UnitTests`. Expected: all pass, including the existing reader tests (the `Text` behaviour is unchanged).

- [ ] **Step 8: Stage** (no commit): `git add src/Lumen.Application/Ingestion src/Lumen.Infrastructure/DocumentReading tests/Lumen.UnitTests`.

---

### Task 2: `TableLayout` — placement, merge, row kinds, columns, column headers

**Files:**
- Create: `src/Lumen.Infrastructure/Chunking/RowKind.cs`, `LayoutCell.cs`, `LayoutRow.cs`, `LayoutColumn.cs`, `TableLayout.cs`
- Test: `tests/Lumen.UnitTests/Chunking/TableLayoutTests.cs`

**Interfaces:**
- Consumes: `TableGrid`, `TableCell` (Task 1); `TableFixtures.LoadAsync` (Task 1).
- Produces:
  - `enum RowKind { Header, Data, GroupLabel, Other }`;
  - `LayoutCell { string Text; int Start; int End }` (grid columns `[Start, End)`);
  - `LayoutRow { List<LayoutCell> Cells; RowKind Kind }`;
  - `record LayoutColumn(int Start, int End)`;
  - `TableLayout`:
    - `static Build(TableGrid) : TableLayout`;
    - `List<LayoutRow> Rows`;
    - `List<LayoutColumn>? Columns` (null = not aligned);
    - `int LabelEnd`;
    - `bool HasHeader`, `bool HasData`;
    - `int ColumnOf(LayoutCell)`;
    - `string ColumnHeader(int column)`;
    - `const int MaxHeaderRows = 6`.

- [ ] **Step 1: Write the failing tests** `TableLayoutTests.cs`:

```csharp
namespace Lumen.UnitTests.Chunking;

public class TableLayoutTests
{
    private static async Task<TableLayout> LayoutOf(string fixture) => TableLayout.Build((await TableFixtures.LoadAsync(fixture)).Table!);

    private static string[] Texts(LayoutRow row) => row.Cells.Select(c => c.Text).ToArray();

    [Fact]
    public async Task Merges_currency_and_percent_cells_into_their_numbers()
    {
        var layout = await LayoutOf("apple-operating-expenses");

        var rnd = layout.Rows.First(r => r.Cells[0].Text == "Research and development");
        Assert.Equal(["Research and development", "$34,550", "10%", "$31,370", "5%", "$29,915"], Texts(rnd));
    }

    [Fact]
    public async Task Finds_the_year_header_and_five_aligned_columns()
    {
        var layout = await LayoutOf("apple-operating-expenses");

        var header = Assert.Single(layout.Rows, r => r.Kind == RowKind.Header);
        Assert.Equal(["2025", "Change", "2024", "Change", "2023"], Texts(header));
        Assert.NotNull(layout.Columns);
        Assert.Equal(5, layout.Columns!.Count);
        Assert.Equal(["2025", "Change", "2024", "Change", "2023"], Enumerable.Range(0, 5).Select(layout.ColumnHeader));
    }

    [Fact]
    public async Task Keeps_empty_change_slots_in_position()
    {
        var layout = await LayoutOf("apple-operating-expenses");

        var share = layout.Rows.First(r => r.Cells[0].Text == "Percentage of total net sales");
        Assert.Equal([0, 2, 4], share.Cells.Skip(1).Select(layout.ColumnOf));
    }

    [Fact]
    public async Task Row_group_labels_before_the_first_data_row_are_not_header_rows()
    {
        var layout = await LayoutOf("apple-balance-sheet");

        Assert.Equal(["September 27, 2025", "September 28, 2024"], Texts(Assert.Single(layout.Rows, r => r.Kind == RowKind.Header)));
        Assert.Equal(["ASSETS:", "Current assets:", "Non-current assets:"], layout.Rows.Where(r => r.Kind == RowKind.GroupLabel).Select(r => r.Cells[0].Text));
    }

    [Fact]
    public async Task A_table_whose_first_row_is_a_group_label_has_no_header()
    {
        var layout = await LayoutOf("apple-gross-margin-percentage");

        Assert.False(layout.HasHeader);
        Assert.Equal(RowKind.GroupLabel, layout.Rows[0].Kind);
        Assert.NotNull(layout.Columns);
    }

    [Fact]
    public async Task Rowspan_cells_push_the_next_rows_to_the_right_and_footnote_cells_are_dropped()
    {
        var layout = await LayoutOf("jpm-selected-data");

        var revenue = layout.Rows.First(r => r.Cells[0].Text == "Total net revenue");
        Assert.Equal(["Total net revenue", "$57,347", "$49,836", "$45,798", "$46,427", "$44,912", "$107,183", "$90,222"], Texts(revenue));
        Assert.Equal("2Q26", layout.ColumnHeader(0));
        Assert.Equal("Six months ended June 30, 2026", layout.ColumnHeader(5));
    }

    [Fact]
    public async Task A_two_level_header_labels_each_column_top_down()
    {
        var layout = await LayoutOf("jpm-capital-ratios");

        Assert.Equal("Standardized, JPMorgan Chase & Co.", layout.ColumnHeader(0));
        Assert.Equal("Advanced, JPMorgan Chase Bank, N.A.", layout.ColumnHeader(3));
    }

    [Fact]
    public async Task A_title_row_stays_in_the_header_block_but_does_not_label_columns()
    {
        var layout = await LayoutOf("jpm-revenue");

        Assert.Equal("Revenue", layout.Rows[0].Cells[0].Text);
        Assert.Equal(RowKind.Header, layout.Rows[0].Kind);
        Assert.Equal("Three months ended June 30, 2026", layout.ColumnHeader(0));
        Assert.Equal("Change", layout.ColumnHeader(2));
    }

    [Fact]
    public async Task A_table_without_data_rows_has_no_header_and_no_columns()
    {
        var layout = await LayoutOf("jpm-trading-arrangements");

        Assert.False(layout.HasData);
        Assert.False(layout.HasHeader);
        Assert.Null(layout.Columns);
    }

    [Fact]
    public void Two_values_of_one_row_in_the_same_column_make_the_table_unaligned()
    {
        var grid = new TableGrid
        {
            Rows =
            [
                new TableRow { Cells = [new TableCell { Text = "A" }, new TableCell { Text = "1", ColSpan = 2 }] },
                new TableRow { Cells = [new TableCell { Text = "B" }, new TableCell { Text = "2" }, new TableCell { Text = "3" }] }
            ]
        };

        Assert.Null(TableLayout.Build(grid).Columns);
    }

    [Fact]
    public void A_rowspan_past_the_last_row_and_ragged_rows_do_not_throw()
    {
        var grid = new TableGrid
        {
            Rows =
            [
                new TableRow { Cells = [new TableCell { Text = "A", RowSpan = 100, ColSpan = 100 }, new TableCell { Text = "1" }] },
                new TableRow { Cells = [new TableCell { Text = "2" }] }
            ]
        };

        var layout = TableLayout.Build(grid);

        Assert.Equal(2, layout.Rows.Count);
        Assert.Equal(100, layout.Rows[1].Cells[0].Start);
    }

    [Fact]
    public void A_year_label_is_a_data_row_label_not_a_header()
    {
        var grid = new TableGrid
        {
            Rows =
            [
                new TableRow { Cells = [new TableCell { Text = "2026" }, new TableCell { Text = "$4,752" }] },
                new TableRow { Cells = [new TableCell { Text = "2027" }, new TableCell { Text = "3,708" }] }
            ]
        };

        Assert.All(TableLayout.Build(grid).Rows, r => Assert.Equal(RowKind.Data, r.Kind));
    }

    [Fact]
    public async Task The_Apple_10K_tables_align_and_find_headers_at_the_measured_rates()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "apple-10k-fy2025.htm"));
        var tables = (await new HtmlDocumentReader().ReadAsync(stream)).Where(e => e.Table is not null).Select(e => TableLayout.Build(e.Table!)).ToList();

        var withData = tables.Where(t => t.HasData).ToList();
        var aligned = withData.Where(t => t.Columns is not null).ToList();
        Assert.True(aligned.Count >= withData.Count * 0.95, $"aligned {aligned.Count} of {withData.Count}");
        Assert.True(aligned.Count(t => t.HasHeader) >= aligned.Count * 0.85, $"with header {aligned.Count(t => t.HasHeader)} of {aligned.Count}");
    }
}
```

- [ ] **Step 2: Run to verify failure.** Run: `dotnet test tests/Lumen.UnitTests --filter FullyQualifiedName~TableLayoutTests`. Expected: compile errors (types missing).

- [ ] **Step 3: Implement.** `RowKind.cs`:

```csharp
namespace Lumen.Infrastructure.Chunking;

public enum RowKind
{
    Header,
    Data,
    GroupLabel,
    Other
}
```

`LayoutCell.cs`:

```csharp
namespace Lumen.Infrastructure.Chunking;

public class LayoutCell
{
    public string Text { get; set; } = string.Empty;
    public int Start { get; set; }
    public int End { get; set; }
}
```

`LayoutRow.cs`:

```csharp
namespace Lumen.Infrastructure.Chunking;

public class LayoutRow
{
    public List<LayoutCell> Cells { get; set; } = [];
    public RowKind Kind { get; set; }
}
```

`LayoutColumn.cs`:

```csharp
namespace Lumen.Infrastructure.Chunking;

public record LayoutColumn(int Start, int End);
```

`TableLayout.cs`:

```csharp
namespace Lumen.Infrastructure.Chunking;

public class TableLayout
{
    public const int MaxHeaderRows = 6;

    private static readonly Regex Numeric = new(@"^[-−–]?\$?\(?\$?\d[\d,]*(?:\.\d+)?%?\)?%?$|^(?:—|–|-|NM|N/A)$", RegexOptions.Compiled);
    private static readonly Regex Year = new(@"^(?:19|20)\d{2}$", RegexOptions.Compiled);
    private static readonly Regex FootnoteMarker = new(@"^\([a-z]{1,2}\)$", RegexOptions.Compiled);
    private static readonly HashSet<string> TrailingSymbols = ["%", ")", ")%", "%)"];

    private TableLayout(List<LayoutRow> rows, List<LayoutColumn>? columns, int labelEnd)
    {
        Rows = rows;
        Columns = columns;
        LabelEnd = labelEnd;
    }

    public List<LayoutRow> Rows { get; }

    public List<LayoutColumn>? Columns { get; }

    public int LabelEnd { get; }

    public bool HasHeader => Rows.Any(r => r.Kind == RowKind.Header);

    public bool HasData => Rows.Any(r => r.Kind == RowKind.Data);

    public static TableLayout Build(TableGrid grid)
    {
        var rows = Place(grid)
            .Select(MergeSymbols)
            .Where(cells => cells.Count > 0)
            .Select(cells => new LayoutRow { Cells = cells })
            .ToList();
        Classify(rows);
        var columns = AlignColumns(rows, out var labelEnd);
        return new TableLayout(rows, columns, labelEnd);
    }

    public int ColumnOf(LayoutCell cell) => Columns!.FindIndex(c => cell.Start < c.End && cell.End > c.Start);

    public string ColumnHeader(int column)
    {
        var target = Columns![column];
        var parts = Rows
            .Where(r => r.Kind == RowKind.Header && !IsTitle(r))
            .SelectMany(r => r.Cells)
            .Where(c => c.Start >= LabelEnd && c.Start < target.End && c.End > target.Start)
            .Select(c => c.Text.TrimEnd(',', ':', ' '));
        return string.Join(", ", parts);
    }

    private bool IsTitle(LayoutRow row) => row.Cells.Count == 1 && row.Cells[0].Start < LabelEnd;

    private static List<List<LayoutCell>> Place(TableGrid grid)
    {
        var occupied = new HashSet<(int Row, int Column)>();
        var placed = new List<List<LayoutCell>>();
        for (var r = 0; r < grid.Rows.Count; r++)
        {
            var column = 0;
            var cells = new List<LayoutCell>();
            foreach (var cell in grid.Rows[r].Cells)
            {
                while (occupied.Contains((r, column)))
                {
                    column++;
                }

                for (var below = r + 1; below < Math.Min(r + cell.RowSpan, grid.Rows.Count); below++)
                {
                    for (var c = column; c < column + cell.ColSpan; c++)
                    {
                        occupied.Add((below, c));
                    }
                }

                cells.Add(new LayoutCell { Text = cell.Text, Start = column, End = column + cell.ColSpan });
                column += cell.ColSpan;
            }

            placed.Add(cells);
        }

        return placed;
    }

    // SEC filings put "$", "%" and ")" in cells of their own and footnote markers like "(e)" between columns.
    private static List<LayoutCell> MergeSymbols(List<LayoutCell> cells)
    {
        var merged = new List<LayoutCell>();
        foreach (var cell in cells.Where(c => c.Text.Length > 0))
        {
            if (merged.Count > 0 && TrailingSymbols.Contains(cell.Text))
            {
                merged[^1].Text += cell.Text;
                merged[^1].End = cell.End;
            }
            else if (merged.Count > 0 && FootnoteMarker.IsMatch(cell.Text))
            {
            }
            else if (merged.Count > 0 && merged[^1].Text == "$")
            {
                merged[^1].Text = "$" + cell.Text;
                merged[^1].End = cell.End;
            }
            else
            {
                merged.Add(new LayoutCell { Text = cell.Text, Start = cell.Start, End = cell.End });
            }
        }

        return merged;
    }

    private static bool IsValue(string text) => Numeric.IsMatch(text) && !Year.IsMatch(text);

    private static bool IsLabel(string text) => !Numeric.IsMatch(text) || Year.IsMatch(text);

    private static bool IsData(LayoutRow row) =>
        row.Cells.Count >= 2 && IsLabel(row.Cells[0].Text) && row.Cells.Skip(1).Any(c => IsValue(c.Text));

    private static bool IsGroupLabel(LayoutRow row) => row.Cells.Count == 1 && !IsValue(row.Cells[0].Text);

    private static void Classify(List<LayoutRow> rows)
    {
        var firstData = rows.FindIndex(IsData);
        var headerEnd = 0;
        if (firstData > 0)
        {
            headerEnd = firstData;
            var labelEnd = rows[firstData].Cells[0].End;
            while (headerEnd > 0 && rows[headerEnd - 1].Cells.Count == 1 && rows[headerEnd - 1].Cells[0].Start < labelEnd)
            {
                headerEnd--;
            }

            if (headerEnd > MaxHeaderRows)
            {
                headerEnd = 0;
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Kind = i < headerEnd ? RowKind.Header
                : IsData(rows[i]) ? RowKind.Data
                : IsGroupLabel(rows[i]) ? RowKind.GroupLabel
                : RowKind.Other;
        }
    }

    private static List<LayoutColumn>? AlignColumns(List<LayoutRow> rows, out int labelEnd)
    {
        var data = rows.Where(r => r.Kind == RowKind.Data).ToList();
        labelEnd = data.Count == 0 ? 0 : data.Max(r => r.Cells[0].End);
        if (data.Count == 0 || data.Select(r => r.Cells[0].Start).Distinct().Count() != 1)
        {
            return null;
        }

        var columns = new List<LayoutColumn>();
        foreach (var (start, end) in data.SelectMany(r => r.Cells.Skip(1)).Select(c => (c.Start, c.End)).Distinct().OrderBy(s => s.Start).ThenBy(s => s.End))
        {
            if (columns.Count > 0 && start < columns[^1].End)
            {
                columns[^1] = columns[^1] with { End = Math.Max(columns[^1].End, end) };
            }
            else
            {
                columns.Add(new LayoutColumn(start, end));
            }
        }

        if (columns[0].Start < labelEnd)
        {
            return null;
        }

        foreach (var row in data)
        {
            var used = new HashSet<int>();
            foreach (var cell in row.Cells.Skip(1))
            {
                if (!used.Add(columns.FindIndex(c => cell.Start < c.End && cell.End > c.Start)))
                {
                    return null;
                }
            }
        }

        return columns;
    }
}
```

The empty `else if` branch drops the footnote cell. If the reviewer finds an empty block unclear, swap it for a `continue` inside a `foreach` with `if` guards. The behaviour is what's pinned.

- [ ] **Step 4: Run the tests.** Run: `dotnet test tests/Lumen.UnitTests --filter FullyQualifiedName~TableLayoutTests`. Expected: PASS. If the Apple-rate test fails, print the counts. Do not loosen the thresholds without recording the measured numbers in the task report.

- [ ] **Step 5: Stage** (no commit): `git add src/Lumen.Infrastructure/Chunking tests/Lumen.UnitTests/Chunking/TableLayoutTests.cs`.

---

### Task 3: `TableFormatter` and `TableCaption`, plus options and the `TableFormat` enum

**Files:**
- Create: `src/Lumen.Infrastructure/Chunking/TableLine.cs`, `FormattedTable.cs`, `TableFormatter.cs`, `TableCaption.cs`, `src/Lumen.Infrastructure/DependencyInjection/TableFormat.cs`
- Modify: `src/Lumen.Infrastructure/DependencyInjection/ChunkingOptions.cs`
- Test: `tests/Lumen.UnitTests/Chunking/TableFormatterTests.cs`, `TableCaptionTests.cs`

**Interfaces:**
- Consumes: `TableLayout`, `RowKind` (Task 2); `DocumentElement.Table` (Task 1).
- Produces:
  - `record TableLine(string Text, bool IsGroupLabel)`;
  - `FormattedTable { string? Caption; List<string> HeaderLines; List<TableLine> BodyLines; bool RepeatHeader; bool ColumnsFellBack }`;
  - `static TableFormatter.Format(DocumentElement table, string? caption, ChunkingOptions options) : FormattedTable`;
  - `static TableCaption.Find(DocumentElement? previous) : string?`, with `TableCaption.Prefix = "Table: "` and `TableCaption.MaxWords = 40`;
  - `enum TableFormat { Pipe, KeyValue }`;
  - `ChunkingOptions.TableCells`, `TableHeaders`, `TableCaptions` (bool) and `TableFormat` (`TableFormat`, default `Pipe`).

- [ ] **Step 1: Options and enum** (needed for the tests to compile). `TableFormat.cs`:

```csharp
namespace Lumen.Infrastructure.DependencyInjection;

public enum TableFormat
{
    Pipe,
    KeyValue
}
```

Add to `ChunkingOptions`:

```csharp
    public bool TableCells { get; set; }
    public bool TableHeaders { get; set; }
    public bool TableCaptions { get; set; }
    public TableFormat TableFormat { get; set; } = TableFormat.Pipe;
```

- [ ] **Step 2: Write the failing tests** `TableFormatterTests.cs`:

```csharp
namespace Lumen.UnitTests.Chunking;

public class TableFormatterTests
{
    private static readonly ChunkingOptions Cells = new() { TableCells = true };
    private static readonly ChunkingOptions CellsAndHeaders = new() { TableCells = true, TableHeaders = true };
    private static readonly ChunkingOptions KeyValue = new() { TableCells = true, TableHeaders = true, TableFormat = TableFormat.KeyValue };

    private static string[] Body(FormattedTable table) => table.BodyLines.Select(l => l.Text).ToArray();

    [Fact]
    public async Task Without_TableCells_the_body_is_todays_text()
    {
        var element = await TableFixtures.LoadAsync("apple-operating-expenses");

        var table = TableFormatter.Format(element, caption: "Operating expenses were as follows:", new ChunkingOptions { TableCaptions = true });

        Assert.Equal(element.Text.Split('\n'), Body(table));
        Assert.Empty(table.HeaderLines);
        Assert.Equal("Operating expenses were as follows:", table.Caption);
        Assert.False(table.RepeatHeader);
    }

    [Fact]
    public async Task Pipe_rows_keep_empty_slots_and_merged_symbols()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("apple-operating-expenses"), null, Cells);

        Assert.Equal(["2025 | Change | 2024 | Change | 2023"], table.HeaderLines);
        Assert.Equal("Research and development | $34,550 | 10% | $31,370 | 5% | $29,915", Body(table)[0]);
        Assert.Equal("Percentage of total net sales | 8% | | 8% | | 8%", Body(table)[1]);
        Assert.False(table.RepeatHeader);
    }

    [Fact]
    public async Task Group_labels_are_marked_as_such()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("jpm-financial-performance"), null, CellsAndHeaders);

        Assert.Contains(table.BodyLines, l => l is { Text: "Capital ratios - Standardized(a)", IsGroupLabel: true });
        Assert.Contains("CET1 capital | 14.2% | 15.1% | | 14.2% | 15.1%", Body(table));
        Assert.Equal(3, table.HeaderLines.Count);
        Assert.True(table.RepeatHeader);
    }

    [Fact]
    public async Task Key_value_rows_carry_their_column_headers_and_group()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("jpm-capital-ratios"), null, KeyValue);

        Assert.Empty(table.HeaderLines);
        Assert.False(table.RepeatHeader);
        Assert.Contains(
            "Risk-based capital metrics › CET1 capital ratio | Standardized, JPMorgan Chase & Co.: 14.2% | Standardized, JPMorgan Chase Bank, N.A.: 15.0% | Advanced, JPMorgan Chase & Co.: 14.2% | Advanced, JPMorgan Chase Bank, N.A.: 16.1%",
            Body(table));
        Assert.DoesNotContain(table.BodyLines, l => l.IsGroupLabel);
    }

    [Fact]
    public async Task Key_value_skips_empty_slots()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("apple-operating-expenses"), null, KeyValue);

        Assert.Equal("Research and development | 2025: $34,550 | Change: 10% | 2024: $31,370 | Change: 5% | 2023: $29,915", Body(table)[0]);
        Assert.Equal("Percentage of total net sales | 2025: 8% | 2024: 8% | 2023: 8%", Body(table)[1]);
    }

    [Fact]
    public async Task Key_value_falls_back_to_pipe_for_a_table_without_a_header()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("apple-gross-margin-percentage"), null, KeyValue);

        Assert.Equal(["Gross margin percentage:", "Products | 36.8% | 37.2% | 36.5%", "Services | 75.4% | 73.9% | 70.8%", "Total gross margin percentage | 46.9% | 46.2% | 44.1%"], Body(table));
    }

    [Fact]
    public async Task A_table_without_data_rows_renders_compact_rows_and_is_not_a_fallback()
    {
        var table = TableFormatter.Format(await TableFixtures.LoadAsync("jpm-trading-arrangements"), null, KeyValue);

        Assert.Equal("Name | Title | Adoption date | Duration(c) | Aggregate number of shares to be sold(d)", Body(table)[0]);
        Assert.Empty(table.HeaderLines);
        Assert.False(table.ColumnsFellBack);
    }

    [Fact]
    public void An_unaligned_table_renders_compact_rows_and_reports_the_fallback()
    {
        var element = new DocumentElement
        {
            Type = ElementType.Table,
            Text = "A | 1\nB | 2 | 3",
            Table = new TableGrid
            {
                Rows =
                [
                    new TableRow { Cells = [new TableCell { Text = "A" }, new TableCell { Text = "1", ColSpan = 2 }] },
                    new TableRow { Cells = [new TableCell { Text = "B" }, new TableCell { Text = "2" }, new TableCell { Text = "3" }] }
                ]
            }
        };

        var table = TableFormatter.Format(element, null, Cells);

        Assert.True(table.ColumnsFellBack);
        Assert.Equal(["A | 1", "B | 2 | 3"], Body(table));
    }

    [Fact]
    public void A_table_without_a_grid_uses_todays_text_even_with_every_switch_on()
    {
        var element = new DocumentElement { Type = ElementType.Table, Text = "Revenue | 10.5 | 9.8" };

        var table = TableFormatter.Format(element, "caption:", KeyValue);

        Assert.Equal(["Revenue | 10.5 | 9.8"], Body(table));
        Assert.Equal("caption:", table.Caption);
        Assert.False(table.RepeatHeader);
    }
}
```

`TableCaptionTests.cs`:

```csharp
namespace Lumen.UnitTests.Chunking;

public class TableCaptionTests
{
    private static DocumentElement Paragraph(string text) => new() { Type = ElementType.Paragraph, Text = text };

    [Fact]
    public void A_short_paragraph_ending_with_a_colon_is_the_caption() =>
        Assert.Equal("Operating expenses for 2025, 2024 and 2023 were as follows (dollars in millions):",
            TableCaption.Find(Paragraph("Operating expenses for 2025, 2024 and 2023 were as follows (dollars in millions):")));

    [Fact]
    public void The_matching_sentence_of_a_long_paragraph_is_cut_to_forty_words()
    {
        const string dimon = "The following table provides information concerning Rule 10b5-1 trading arrangements (as defined in Item 408 of Regulation S-K under the Securities Exchange Act of 1934) adopted in the fourth quarter of 2025 by any director or officer who is subject to the filing requirements of Section 16 of the Securities Exchange Act of 1934 (each a “Section 16 Director or Officer”). These trading arrangements are intended to satisfy the affirmative defense of Rule 10b5-1(c). No non-Rule 10b5-1 trading arrangements were adopted.";

        var caption = TableCaption.Find(Paragraph(dimon))!;

        Assert.StartsWith("The following table provides information concerning Rule 10b5-1 trading arrangements", caption);
        Assert.EndsWith(" …", caption);
        Assert.Equal(TableCaption.MaxWords + 1, caption.Split(' ').Length);
    }

    [Fact]
    public void A_paragraph_without_a_marker_or_a_colon_is_not_a_caption() =>
        Assert.Null(TableCaption.Find(Paragraph("Net sales increased primarily due to higher iPhone revenue.")));

    [Fact]
    public void A_heading_or_a_table_before_the_table_gives_no_caption()
    {
        Assert.Null(TableCaption.Find(new DocumentElement { Type = ElementType.Heading, Text = "Summary of results:" }));
        Assert.Null(TableCaption.Find(new DocumentElement { Type = ElementType.Table, Text = "A | 1" }));
        Assert.Null(TableCaption.Find(null));
    }

    [Fact]
    public void A_huge_unpunctuated_paragraph_is_still_cut_to_forty_words()
    {
        var text = "The following table shows " + string.Join(" ", Enumerable.Repeat("item", 500));

        Assert.Equal(TableCaption.MaxWords + 1, TableCaption.Find(Paragraph(text))!.Split(' ').Length);
    }
}
```

- [ ] **Step 3: Run to verify failure.** Run: `dotnet test tests/Lumen.UnitTests --filter "FullyQualifiedName~TableFormatterTests|FullyQualifiedName~TableCaptionTests"`. Expected: compile errors.

- [ ] **Step 4: Implement.** `TableLine.cs`:

```csharp
namespace Lumen.Infrastructure.Chunking;

public record TableLine(string Text, bool IsGroupLabel);
```

`FormattedTable.cs`:

```csharp
namespace Lumen.Infrastructure.Chunking;

public class FormattedTable
{
    public string? Caption { get; init; }
    public List<string> HeaderLines { get; init; } = [];
    public List<TableLine> BodyLines { get; init; } = [];
    public bool RepeatHeader { get; init; }
    public bool ColumnsFellBack { get; init; }
}
```

`TableFormatter.cs`:

```csharp
namespace Lumen.Infrastructure.Chunking;

public static class TableFormatter
{
    public static FormattedTable Format(DocumentElement table, string? caption, ChunkingOptions options)
    {
        if (!options.TableCells || table.Table is null)
        {
            return new FormattedTable { Caption = caption, BodyLines = table.Text.Split('\n').Select(line => new TableLine(line, false)).ToList() };
        }

        var layout = TableLayout.Build(table.Table);
        var keyValue = options.TableFormat == TableFormat.KeyValue && layout.Columns is not null && layout.HasHeader;
        List<string> headerLines = keyValue ? [] : layout.Rows.Where(r => r.Kind == RowKind.Header).Select(Compact).ToList();
        var body = new List<TableLine>();
        string? group = null;

        foreach (var row in layout.Rows.Where(r => r.Kind != RowKind.Header))
        {
            switch (row.Kind)
            {
                case RowKind.GroupLabel when keyValue:
                    group = row.Cells[0].Text.TrimEnd(':', ' ');
                    break;
                case RowKind.GroupLabel:
                    body.Add(new TableLine(Compact(row), true));
                    break;
                case RowKind.Data when keyValue:
                    body.Add(new TableLine(KeyValueLine(row, layout, group), false));
                    break;
                case RowKind.Data when layout.Columns is not null:
                    body.Add(new TableLine(AlignedLine(row, layout), false));
                    break;
                default:
                    body.Add(new TableLine(Compact(row), false));
                    break;
            }
        }

        return new FormattedTable
        {
            Caption = caption,
            HeaderLines = headerLines,
            BodyLines = body,
            RepeatHeader = options.TableHeaders && !keyValue,
            ColumnsFellBack = layout.HasData && layout.Columns is null
        };
    }

    private static string Compact(LayoutRow row) => string.Join(" | ", row.Cells.Select(c => c.Text));

    // CleanText has already collapsed whitespace inside cells, so a double space can only come from an empty slot.
    private static string AlignedLine(LayoutRow row, TableLayout layout)
    {
        var slots = Enumerable.Repeat(string.Empty, layout.Columns!.Count).ToArray();
        foreach (var cell in row.Cells.Skip(1))
        {
            slots[layout.ColumnOf(cell)] = cell.Text;
        }

        var last = Array.FindLastIndex(slots, s => s.Length > 0);
        return string.Join(" | ", slots.Take(last + 1).Prepend(row.Cells[0].Text)).Replace("  ", " ");
    }

    private static string KeyValueLine(LayoutRow row, TableLayout layout, string? group)
    {
        var label = group is null ? row.Cells[0].Text : $"{group} › {row.Cells[0].Text}";
        var values = row.Cells.Skip(1).Select(cell =>
        {
            var header = layout.ColumnHeader(layout.ColumnOf(cell));
            return header.Length == 0 ? cell.Text : $"{header}: {cell.Text}";
        });
        return string.Join(" | ", values.Prepend(label));
    }
}
```

`TableCaption.cs`:

```csharp
namespace Lumen.Infrastructure.Chunking;

public static class TableCaption
{
    public const string Prefix = "Table: ";
    public const int MaxWords = 40;

    private static readonly Regex Marker = new("following table|as follows|summar", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+(?=[A-Z(“""])", RegexOptions.Compiled);

    public static string? Find(DocumentElement? previous)
    {
        if (previous is not { Type: ElementType.Paragraph } || string.IsNullOrWhiteSpace(previous.Text))
        {
            return null;
        }

        var sentences = SentenceBreak.Split(previous.Text.Trim());
        var sentence = sentences.LastOrDefault(s => Marker.IsMatch(s)) ?? (sentences[^1].EndsWith(':') ? sentences[^1] : null);
        if (sentence is null)
        {
            return null;
        }

        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= MaxWords ? sentence : string.Join(' ', words.Take(MaxWords)) + " …";
    }
}
```

`TableFormatter` uses `ChunkingOptions`/`TableFormat` from `Lumen.Infrastructure.DependencyInjection`, which is already global in Infrastructure.

- [ ] **Step 5: Run the tests.** Run: `dotnet test tests/Lumen.UnitTests --filter "FullyQualifiedName~TableFormatterTests|FullyQualifiedName~TableCaptionTests"`. Expected: PASS. Then run the whole unit suite: green.

- [ ] **Step 6: Stage** (no commit).

---

### Task 4: Chunker uses the formatted table; validator, profile, settings, appsettings, integration pins

**Files:**
- Modify: `src/Lumen.Infrastructure/Chunking/Chunker.cs`, `IngestProfileProvider.cs`, `src/Lumen.Infrastructure/DependencyInjection/ChunkingOptionsValidator.cs`, `src/Lumen.Api/Controllers/SettingsController.cs`, `src/Lumen.Api/DTO/Responses/SettingsDto.cs`, `src/Lumen.Api/appsettings.json`, `tests/Lumen.IntegrationTests/Utils/TestWebApplicationFactory.cs`
- Test: `tests/Lumen.UnitTests/Chunking/ChunkerTests.cs`, `ChunkingOptionsValidatorTests.cs`, `IngestProfileProviderTests.cs`, `tests/Lumen.IntegrationTests/Settings/SettingsEndpointTests.cs`, `tests/Lumen.IntegrationTests/Documents/DocumentIngestionCharacterizationTests.cs`

**Interfaces:**
- Consumes: `TableFormatter.Format`, `FormattedTable`, `TableLine`, `TableCaption.Find`/`Prefix` (Task 3); `ChunkingOptions` table switches (Task 3).
- Produces:
  - `Chunker(IOptions<ChunkingOptions> options, ILogger<Chunker> logger)` (a new constructor parameter);
  - profile tokens `;cells=v1`, `;thdr=v1`, `;tcap=v1`, `;tser=kv1`;
  - `ChunkingSettingsDto.TableCells`, `TableHeaders`, `TableCaptions` (bool) and `TableFormat` (string).

- [ ] **Step 1: Update `ChunkerTests.CreateChunker`** so it compiles with the new constructor and can set the table switches:

```csharp
    private static Chunker CreateChunker(int maxTokens = 250, int overlapTokens = 60, bool contextHeaders = false, int mergeBelowTokens = 0,
        bool protectDecimals = false, bool tableCells = false, bool tableHeaders = false, bool tableCaptions = false, TableFormat tableFormat = TableFormat.Pipe) =>
        new(Options.Create(new ChunkingOptions
        {
            MaxTokensPerChunk = maxTokens,
            OverlapTokens = overlapTokens,
            ContextHeaders = contextHeaders,
            MergeBelowTokens = mergeBelowTokens,
            ProtectDecimals = protectDecimals,
            TableCells = tableCells,
            TableHeaders = tableHeaders,
            TableCaptions = tableCaptions,
            TableFormat = tableFormat
        }), NullLogger<Chunker>.Instance);
```

(`Microsoft.Extensions.Logging.Abstractions` is already global in UnitTests.)

- [ ] **Step 2: Write the failing chunker tests** (append to `ChunkerTests.cs`):

```csharp
    [Fact]
    public async Task With_table_switches_off_a_grid_carrying_table_chunks_exactly_as_before()
    {
        var element = await TableFixtures.LoadAsync("jpm-financial-performance");
        var gridless = new DocumentElement { Type = ElementType.Table, Text = element.Text };

        var withGrid = CreateChunker(protectDecimals: true).Chunk(Guid.NewGuid(), [element], Context);
        var withoutGrid = CreateChunker(protectDecimals: true).Chunk(Guid.NewGuid(), [gridless], Context);

        Assert.Equal(withoutGrid.Select(c => c.Text), withGrid.Select(c => c.Text));
    }

    [Fact]
    public async Task Continuation_chunks_repeat_the_header_and_the_open_row_group()
    {
        var element = await TableFixtures.LoadAsync("jpm-financial-performance");
        var header = TableFormatter.Format(element, null, new ChunkingOptions { TableCells = true }).HeaderLines;

        var chunks = CreateChunker(tableCells: true, tableHeaders: true).Chunk(Guid.NewGuid(), [element], Context);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.StartsWith(string.Join("\n", header), c.Text));
        var cet1 = Assert.Single(chunks, c => c.Text.Contains("CET1 capital | 14.2%"));
        Assert.Contains("Capital ratios - Standardized(a)", cet1.Text);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 250));
    }

    [Fact]
    public async Task Without_TableHeaders_only_the_first_chunk_has_the_header()
    {
        var element = await TableFixtures.LoadAsync("jpm-financial-performance");

        var chunks = CreateChunker(tableCells: true).Chunk(Guid.NewGuid(), [element], Context);

        Assert.StartsWith("Financial performance of JPMorganChase", chunks[0].Text);
        Assert.DoesNotContain(chunks.Skip(1), c => c.Text.StartsWith("Financial performance of JPMorganChase"));
    }

    [Fact]
    public void A_row_group_label_never_ends_a_table_chunk()
    {
        var rows = Enumerable.Range(0, 60).Select(i => new TableRow { Cells = [new TableCell { Text = $"Line item {i}" }, new TableCell { Text = $"{i},000" }] }).ToList();
        rows.Insert(0, new TableRow { Cells = [new TableCell { Text = "" }, new TableCell { Text = "2026" }] });
        for (var i = 10; i < rows.Count; i += 10)
            rows.Insert(i, new TableRow { Cells = [new TableCell { Text = $"Group {i}" }] });
        var element = new DocumentElement { Type = ElementType.Table, Text = "x", Table = new TableGrid { Rows = rows } };

        var chunks = CreateChunker(maxTokens: 60, overlapTokens: 0, tableCells: true, tableHeaders: true).Chunk(Guid.NewGuid(), [element], Context);

        Assert.True(chunks.Count > 3);
        Assert.DoesNotContain(chunks, c => c.Text.Split('\n')[^1].StartsWith("Group "));
    }

    [Fact]
    public async Task A_header_bigger_than_half_the_budget_is_not_repeated_and_the_budget_holds()
    {
        var element = await TableFixtures.LoadAsync("jpm-financial-performance");

        var chunks = CreateChunker(maxTokens: 80, overlapTokens: 0, tableCells: true, tableHeaders: true).Chunk(Guid.NewGuid(), [element], Context);

        Assert.All(chunks, c => Assert.True(c.TokenCount <= 80, $"{c.TokenCount} tokens"));
        Assert.Contains(chunks.Skip(1), c => !c.Text.StartsWith("Financial performance of JPMorganChase"));
    }

    [Fact]
    public async Task The_caption_is_copied_into_every_table_chunk_and_stays_in_the_prose()
    {
        var caption = new DocumentElement { Type = ElementType.Paragraph, Text = "The following table summarizes selected financial performance:" };
        var table = await TableFixtures.LoadAsync("jpm-financial-performance");

        var chunks = CreateChunker(tableCells: true, tableHeaders: true, tableCaptions: true).Chunk(Guid.NewGuid(), [caption, table], Context);

        Assert.Contains("The following table summarizes", chunks[0].Text);
        Assert.Equal(ChunkType.Prose, chunks[0].ChunkType);
        Assert.All(chunks.Where(c => c.ChunkType == ChunkType.Table),
            c => Assert.StartsWith("Table: The following table summarizes selected financial performance:", c.Text));
    }

    [Fact]
    public void A_pdf_table_without_a_grid_gets_its_caption_and_keeps_decimals_whole()
    {
        var caption = new DocumentElement { Type = ElementType.Paragraph, Text = "Revenue by segment was as follows:" };
        var table = new DocumentElement { Type = ElementType.Table, Text = "Segment | 2025\nCloud | 10.7" };

        var chunks = CreateChunker(protectDecimals: true, tableCells: true, tableHeaders: true, tableCaptions: true, tableFormat: TableFormat.KeyValue)
            .Chunk(Guid.NewGuid(), [caption, table], Context);

        var tableChunk = Assert.Single(chunks, c => c.ChunkType == ChunkType.Table);
        Assert.Equal("Table: Revenue by segment was as follows:\nSegment | 2025\nCloud | 10.7", tableChunk.Text);
    }
```

Also append to `ChunkingOptionsValidatorTests.cs`:

```csharp
    [Theory]
    [InlineData(false, true, TableFormat.Pipe)]
    [InlineData(false, false, TableFormat.KeyValue)]
    public void Table_headers_and_key_value_need_TableCells(bool cells, bool headers, TableFormat format) =>
        Assert.True(_validator.Validate(null, new ChunkingOptions { TableCells = cells, TableHeaders = headers, TableFormat = format }).Failed);

    [Fact]
    public void Captions_alone_are_valid() =>
        Assert.True(_validator.Validate(null, new ChunkingOptions { TableCaptions = true }).Succeeded);
```

And to `IngestProfileProviderTests.cs`:

```csharp
    [Fact]
    public void Table_switches_add_tokens_only_when_on()
    {
        Assert.Equal("chunk=250/60;hdr=v1;merge=0;dec=v1", Profile(new ChunkingOptions { ContextHeaders = true, ProtectDecimals = true }));
        Assert.Equal("chunk=250/60;hdr=v1;merge=0;dec=v1;cells=v1;thdr=v1;tcap=v1;tser=kv1", Profile(new ChunkingOptions
        {
            ContextHeaders = true, ProtectDecimals = true, TableCells = true, TableHeaders = true, TableCaptions = true, TableFormat = TableFormat.KeyValue
        }));
        Assert.Equal("chunk=250/60;hdr=off;merge=0;dec=off;tcap=v1", Profile(new ChunkingOptions { TableCaptions = true }));
    }
```

- [ ] **Step 3: Run to verify failure.** Run: `dotnet test tests/Lumen.UnitTests --filter "FullyQualifiedName~Chunk|FullyQualifiedName~IngestProfile"`. Expected: compile error on the `Chunker` constructor; once it compiles, the new tests fail.

- [ ] **Step 4: Implement the validator rules** (inside `Validate`, before the return):

```csharp
        if (options.TableHeaders && !options.TableCells)
        {
            failures.Add("Chunking:TableHeaders requires Chunking:TableCells.");
        }

        if (options.TableFormat == TableFormat.KeyValue && !options.TableCells)
        {
            failures.Add("Chunking:TableFormat=KeyValue requires Chunking:TableCells.");
        }
```

- [ ] **Step 5: Implement the profile:**

```csharp
    public IngestProfileProvider(IOptions<ChunkingOptions> options)
    {
        var o = options.Value;
        var profile = new StringBuilder($"chunk={o.MaxTokensPerChunk}/{o.OverlapTokens};hdr={(o.ContextHeaders ? "v1" : "off")};merge={o.MergeBelowTokens};dec={(o.ProtectDecimals ? "v1" : "off")}");
        // Table switches are appended only when on, so a corpus ingested before they existed keeps a matching profile.
        if (o.TableCells) profile.Append(";cells=v1");
        if (o.TableHeaders) profile.Append(";thdr=v1");
        if (o.TableCaptions) profile.Append(";tcap=v1");
        if (o.TableFormat == TableFormat.KeyValue) profile.Append(";tser=kv1");
        Current = profile.ToString();
    }
```

Match the file's brace style. If the codebase avoids single-line `if`, use braces.

- [ ] **Step 6: Implement the chunker changes.** Fields and constructor:

```csharp
    private readonly ChunkingOptions _options;
    private readonly bool _formatTables;
    private readonly ILogger<Chunker> _logger;

    public Chunker(IOptions<ChunkingOptions> options, ILogger<Chunker> logger)
    {
        _options = options.Value;
        _maxTokensPerChunk = options.Value.MaxTokensPerChunk;
        _overlapTokens = options.Value.OverlapTokens;
        _contextHeaders = options.Value.ContextHeaders;
        _mergeBelowTokens = options.Value.MergeBelowTokens;
        _protectDecimals = options.Value.ProtectDecimals;
        _formatTables = options.Value.TableCells || options.Value.TableCaptions;
        _logger = logger;
    }
```

The loop in `Chunk`, tracking the previous non-blank element:

```csharp
        DocumentElement? previous = null;
        foreach (var element in elements)
        {
            if (element.Type == ElementType.Table)
            {
                FlushProseRun();
                if (string.IsNullOrWhiteSpace(element.Text))
                    continue;

                var tableTexts = _formatTables
                    ? SplitFormattedTable(FormatTable(element, previous))
                    : SplitTableIfOversized(Protect(element.Text));
                foreach (var tableChunkText in tableTexts)
                    AddChunk(tableChunkText, ChunkType.Table);
                previous = element;
                continue;
            }

            if (string.IsNullOrWhiteSpace(element.Text))
                continue;

            previous = element;

            if (_contextHeaders && (element.Type == ElementType.Heading || SectionHeading.IsHeadingLike(element.Text)))
            {
                FlushProseRun();
                section = element.Text.Trim();
            }

            proseLines.AddRange(TextChunker.SplitPlainTextLines(Protect(element.Text), Math.Max(1, _maxTokensPerChunk / 4), CountTokens));
        }
```

Keep the existing comment above `proseLines.AddRange`. New private methods:

```csharp
    private FormattedTable FormatTable(DocumentElement table, DocumentElement? previous)
    {
        var formatted = TableFormatter.Format(table, _options.TableCaptions ? TableCaption.Find(previous) : null, _options);
        if (formatted.ColumnsFellBack)
        {
            _logger.LogDebug("Table columns could not be aligned; the table keeps its merged cells without empty slots.");
        }

        return formatted;
    }

    // Every table chunk starts with the caption; continuations also repeat the header rows and the row group they
    // open in, so a number never reaches the index without the column and group that give it meaning.
    private IEnumerable<string> SplitFormattedTable(FormattedTable table)
    {
        var caption = table.Caption is null ? null : Protect(TableCaption.Prefix + table.Caption);
        var header = table.HeaderLines.Select(Protect).ToList();
        var body = table.BodyLines
            .SelectMany(line => SplitOversizedRow(Protect(line.Text)).Select((piece, i) => new TableLine(piece, line.IsGroupLabel && i == 0)))
            .ToList();

        var whole = JoinLines(Prefix(caption, header), body);
        if (CountTokens(whole) <= _maxTokensPerChunk)
        {
            yield return whole;
            yield break;
        }

        caption = caption is null ? null : FitCaption(caption);
        var prefix = Prefix(caption, header);
        var current = new List<TableLine>();
        string? group = null;

        foreach (var line in body)
        {
            if (current.Count > 0 && CountTokens(JoinLines(prefix, current.Append(line))) > _maxTokensPerChunk)
            {
                var dangling = current.Count > 1 && current[^1].IsGroupLabel ? current[^1] : null;
                if (dangling is not null)
                {
                    current.RemoveAt(current.Count - 1);
                }

                yield return JoinLines(prefix, current);
                current = dangling is null ? [] : [dangling];
                prefix = ContinuationPrefix(caption, header, line.IsGroupLabel || dangling is not null ? null : group, table.RepeatHeader);
            }

            current.Add(line);
            if (line.IsGroupLabel)
            {
                group = line.Text;
            }
        }

        if (current.Count > 0)
        {
            yield return JoinLines(prefix, current);
        }
    }

    private List<string> ContinuationPrefix(string? caption, List<string> header, string? group, bool repeatHeader)
    {
        List<string> prefix = caption is null ? [] : [caption];
        if (!repeatHeader)
        {
            return prefix;
        }

        List<string> repeated = group is null ? [.. header] : [.. header, group];
        if (repeated.Count > 0 && CountTokens(string.Join("\n", repeated)) <= _maxTokensPerChunk / 2)
        {
            prefix.AddRange(repeated);
        }

        return prefix;
    }

    private string FitCaption(string caption)
    {
        var words = caption.Split(' ');
        var count = words.Length;
        while (count > 1 && CountTokens(string.Join(' ', words.Take(count))) > _maxTokensPerChunk / 2)
        {
            count--;
        }

        return count == words.Length ? caption : string.Join(' ', words.Take(count)) + " …";
    }

    private static List<string> Prefix(string? caption, List<string> header) => caption is null ? [.. header] : [caption, .. header];

    private static string JoinLines(IEnumerable<string> prefix, IEnumerable<TableLine> lines) =>
        string.Join("\n", prefix.Concat(lines.Select(l => l.Text)));
```

- `EnforceBudget` still runs afterwards and guarantees the budget if a first chunk's header plus first row overshoots.
- `ILogger` comes from `Microsoft.Extensions.Logging`, already global in Infrastructure.
- `Microsoft.Extensions.Logging.Abstractions` is global in UnitTests. If the Infrastructure DI registration (`AddScoped<IChunker, Chunker>`) can't resolve the logger, check that logging is registered in the API host (it is, via the NLog setup).

- [ ] **Step 7: Settings output and appsettings.** In `ChunkingSettingsDto` add:

```csharp
    public bool TableCells { get; set; }
    public bool TableHeaders { get; set; }
    public bool TableCaptions { get; set; }
    public string TableFormat { get; set; } = default!;
```

In `SettingsController.Get()` fill them: `TableCells = _chunking.TableCells`, `TableHeaders = _chunking.TableHeaders`, `TableCaptions = _chunking.TableCaptions`, `TableFormat = _chunking.TableFormat.ToString()`.

In `src/Lumen.Api/appsettings.json` add to the `Chunking` section, after `ProtectDecimals`:

```json
    "TableCells": false,
    "TableHeaders": false,
    "TableCaptions": false,
    "TableFormat": "Pipe"
```

- [ ] **Step 8: Integration pins and tests.** In `TestWebApplicationFactory`, after the `ProtectDecimals` pin:

```csharp
            builder.UseSetting("Chunking:TableCells", "false");
            builder.UseSetting("Chunking:TableHeaders", "false");
            builder.UseSetting("Chunking:TableCaptions", "false");
            builder.UseSetting("Chunking:TableFormat", "Pipe");
```

Extend the existing comment's reasoning to cover them (a single line: tables have dedicated tests). In `SettingsEndpointTests.Returns_the_effective_configuration_without_secrets` add:

```csharp
        Assert.False(root.GetProperty("chunking").GetProperty("tableCells").GetBoolean());
        Assert.Equal("Pipe", root.GetProperty("chunking").GetProperty("tableFormat").GetString());
```

In `DocumentIngestionCharacterizationTests` add a test modeled on the `ProtectDecimals` one at line ~126. Upload the Apple 10-K through a factory with `Chunking:TableCells=true`, `Chunking:TableHeaders=true`, `Chunking:TableCaptions=true`, then assert:
- upload `201`;
- every chunk `TokenCount <= 250`;
- at least one `Table` chunk starts with `"Table: "`;
- some `Table` chunk contains `"Research and development | $34,550 | 10% | $31,370"`;
- the document's `ingestProfile` ends with `";cells=v1;thdr=v1;tcap=v1"`.

Write it out in full in that file's style (same upload helper code as the first test).

- [ ] **Step 9: Run.** Run `dotnet test tests/Lumen.UnitTests`, then `dotnet test tests/Lumen.IntegrationTests` (Docker). Expected: all green, 0 warnings in `dotnet build`.

- [ ] **Step 10: Stage** (no commit).

---

### Task 5: `Chat:SourceContextHeader` — context header in the `/api/ask` prompt

**Files:**
- Modify: `src/Lumen.Infrastructure/DependencyInjection/ChatSettingsOptions.cs`, `src/Lumen.Application/Constants/PromptConstants.cs`, `src/Lumen.Application/Ask/RagPrompt.cs`, `src/Lumen.Application/Ask/AskQuestionQuery.cs`, `src/Lumen.Api/Controllers/AskController.cs`, `src/Lumen.Api/Controllers/SettingsController.cs`, `src/Lumen.Api/DTO/Responses/SettingsDto.cs`, `src/Lumen.Api/appsettings.json`, `tests/Lumen.IntegrationTests/Utils/TestWebApplicationFactory.cs`
- Test: `tests/Lumen.UnitTests/Ask/RagPromptTests.cs`, `tests/Lumen.UnitTests/Ask/AskQuestionQueryHandlerTests.cs`, `tests/Lumen.IntegrationTests/Settings/SettingsEndpointTests.cs`

**Interfaces:**
- Produces:
  - `ChatSettingsOptions.SourceContextHeader` (bool, default false);
  - `PromptConstants.UserTemplateVersionWithContext = "sources-xml-v2"`;
  - `RagPrompt.BuildUserMessage(string question, IReadOnlyList<ChunkSearchResult> sources, bool sourceContextHeader = false)`;
  - `RagPrompt.HashFor(bool sourceContextHeader) : string`, which replaces `RagPrompt.Hash`;
  - `AskQuestionQuery(string Question, Guid? DocumentId, int TopK, bool SourceContextHeader = false)`;
  - `ChatSettingsDto.SourceContextHeader`.

- [ ] **Step 1: Write the failing tests.** In `RagPromptTests.cs`, give the `Chunk` helper an optional `string? contextHeader = null` parameter that sets `ContextHeader`. Then append:

```csharp
    [Fact]
    public void Adds_the_context_header_as_an_escaped_attribute_when_asked()
    {
        var message = RagPrompt.BuildUserMessage("q", [Chunk("text", contextHeader: "Apple 10-K (filed 2025-10-31) — \"Gross Margin\"")], sourceContextHeader: true);

        Assert.Contains("kind=\"prose\" context=\"Apple 10-K (filed 2025-10-31) — &quot;Gross Margin&quot;\">", message);
    }

    [Fact]
    public void Omits_the_attribute_when_off_or_when_the_chunk_has_no_header()
    {
        Assert.DoesNotContain("context=", RagPrompt.BuildUserMessage("q", [Chunk("text", contextHeader: "Header")]));
        Assert.DoesNotContain("context=", RagPrompt.BuildUserMessage("q", [Chunk("text")], sourceContextHeader: true));
    }

    [Fact]
    public void The_hash_without_the_context_header_is_unchanged_and_differs_with_it()
    {
        Assert.Equal("ae0bb62cae85", RagPrompt.HashFor(false));
        Assert.Matches("^[0-9a-f]{12}$", RagPrompt.HashFor(true));
        Assert.NotEqual(RagPrompt.HashFor(false), RagPrompt.HashFor(true));
    }
```

Replace the existing `RagPrompt.Hash` assertion (line ~80) with `RagPrompt.HashFor(false)`. In `AskQuestionQueryHandlerTests` add a test: with a mocked `IChatService` capturing the user message, `new AskQuestionQuery("q", null, 5, SourceContextHeader: true)` over a search result whose `ContextHeader = "H"` sends a message containing `context="H"`. Use the file's existing mock setup for search and embeddings.

- [ ] **Step 2: Run to verify failure.** Run: `dotnet test tests/Lumen.UnitTests --filter FullyQualifiedName~Ask`. Expected: compile errors.

- [ ] **Step 3: Implement.** `PromptConstants`: keep `UserTemplateVersion` and add

```csharp
    public const string UserTemplateVersionWithContext = "sources-xml-v2";
```

Update the comment above `UserTemplateVersion` to mention both versions and that the context attribute selects v2. In `RagPrompt`, replace `Hash`:

```csharp
    private static readonly string HashWithoutContext = ComputeHash(PromptConstants.UserTemplateVersion);
    private static readonly string HashWithContext = ComputeHash(PromptConstants.UserTemplateVersionWithContext);

    public static string HashFor(bool sourceContextHeader) => sourceContextHeader ? HashWithContext : HashWithoutContext;

    private static string ComputeHash(string templateVersion) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            PromptConstants.SystemPrompt + "\n" + PromptConstants.RulesReminder + "\n" + templateVersion)))[..12];
```

`BuildUserMessage(string question, IReadOnlyList<ChunkSearchResult> sources, bool sourceContextHeader = false)`: after the `kind` attribute,

```csharp
            builder.Append("\" kind=\"").Append(source.ChunkType.ToString().ToLowerInvariant()).Append('"');
            if (sourceContextHeader && source.ContextHeader is not null)
            {
                builder.Append(" context=\"").Append(EscapeAttribute(source.ContextHeader)).Append('"');
            }

            builder.AppendLine(">");
```

This must produce exactly today's output when off, so the existing test asserting `kind="prose">` still passes.

`AskQuestionQuery`: `public record AskQuestionQuery(string Question, Guid? DocumentId, int TopK, bool SourceContextHeader = false) : IRequest<AskAnswer>;`. Thread it into `GenerateAsync(request.Question, found, request.SourceContextHeader, ct)` → `RagPrompt.BuildUserMessage(question, sources, sourceContextHeader)`.

`ChatSettingsOptions`: `public bool SourceContextHeader { get; set; }`.

`AskController`: `new AskQuestionQuery(request.Question, request.DocumentId, _chatOptions.ContextTopK, _chatOptions.SourceContextHeader)`.

`SettingsController`: `PromptHash = RagPrompt.HashFor(_chat.SourceContextHeader)`, and `SourceContextHeader = _chat.SourceContextHeader` in `ChatSettingsDto` (add the property).

`appsettings.json` `Chat`: `"SourceContextHeader": false`.

Integration factory: `builder.UseSetting("Chat:SourceContextHeader", "false");`.

`SettingsEndpointTests`: assert `chat.sourceContextHeader` is false.

- [ ] **Step 4: Run.** Unit suite, then integration suite. Expected: green, 0 warnings.

- [ ] **Step 5: Stage** (no commit).

---

### Task 6: Docs, measurement checklist, final verification

**Files:**
- Modify: `CLAUDE.md` (Commands → the re-upload rule mentions the table switches; a short "Stage 6B in progress" status line, *without* results), `.research/roadmap.md` (tick the implementation items)
- Create: `.research/checklists/stage-6b-measurement.md`

- [ ] **Step 1:** CLAUDE.md:
  - In the "Re-upload rule" bullet, add `Chunking:TableCells/TableHeaders/TableCaptions/TableFormat`.
  - Add a bullet: "`Chat:SourceContextHeader` adds `context=\"…\"` to `/api/ask` sources and switches `promptHash`; no re-upload".
  - Add one status paragraph: "Stage 6B (tables) implemented, measurements pending", listing the switches. No numbers, since none are measured yet.
- [ ] **Step 2:** `.research/checklists/stage-6b-measurement.md`: the t0…t4 ladder from design §6. For each rung list:
  - exact user-secrets / env commands (`dotnet user-secrets set "Chunking:TableCells" "true" --project src/Lumen.Api`);
  - the re-upload step (delete + upload all four filings with the same titles);
  - the harness commands (`dotnet run -- run --mode retrieval --label t1-cells`, then `--mode full`, then `dotnet run -- compare runs/<t0> runs/<t1>`);
  - what to read (§6 list).
- [ ] **Step 3: Final verification.**
  - Run `dotnet build` → 0 warnings.
  - Run `dotnet test` → all green. Report the counts per project.
  - Run `grep -rn "IgnoreQueryFilters" src` → only `UserLookupService`.
  - Run `git diff --stat` and check no file outside the File map changed.
- [ ] **Step 4: Stage** (no commit).

---

## After implementation (Claude, with the user's approval at the marked points)

These steps are not plan tasks for the implementer subagents:

1. **Eval-set extension (design §5).**
   - Claude writes 3–5 JPM questions whose answer row sits mid-table, in long tables of the 10-K/10-Q, from the source HTML. Candidates: rows of `jpm-financial-performance` (e.g. tangible book value per share, Tier 1 capital ratio), of the capital-ratios table and of the average-balance tables.
   - They go to `.research/stage-6/eval-candidates.md`.
   - Claude checks each anchor against the current corpus. Each anchor needs ≥ 1 and ≤ 5 matching chunks in its tenant: read-only `SELECT "Text" FROM "DocumentChunks"` through `docker exec … psql`, matched with the harness's `AnchorMatcher` semantics (every substring of one anchor list present).
   - **The user approves the questions** before they're added to `tools/Lumen.Eval/eval-set.json`.
2. **Measurement ladder t0…t4 + holdout** per `.research/checklists/stage-6b-measurement.md`, run by Claude (the user delegated this on 2026-10-11).
   - Results go to `.research/discussion/stage-6-hands-on-findings.md`.
   - Winners become `appsettings.json` defaults.
   - `DECISIONS.md` entries only with numbers. CLAUDE.md status gets updated with them.
