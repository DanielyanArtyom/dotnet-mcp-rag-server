namespace Lumen.Eval.Tests.EvalSets;

public class EvalSetLoaderTests
{
    private static string Write(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"eval-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string Valid = """
        {
          "version": 1,
          "questions": [
            { "id": "q1", "tenant": "acme", "category": "table-value", "question": "What were total net sales in fiscal 2025?",
              "expected": { "kind": "number", "value": 416161, "unit": "millions" },
              "anchors": [["Total net sales", "416,161"]] },
            { "id": "q2", "tenant": "globex", "category": "unanswerable", "question": "What is Apple's CET1 ratio?",
              "expected": { "kind": "refusal" }, "anchors": [] },
            { "id": "q3", "tenant": "acme", "category": "prose", "question": "How does Apple hedge FX risk?",
              "expected": { "kind": "text", "text": "With forwards, options and cross-currency swaps." },
              "anchors": [["cross-currency swaps"]], "holdout": true }
          ]
        }
        """;

    [Fact]
    public void Loads_a_valid_set_and_hashes_the_file_bytes()
    {
        var path = Write(Valid);

        var (set, sha) = EvalSetLoader.Load(path);

        Assert.Equal(3, set.Questions.Count);
        Assert.Equal(ExpectedKind.Number, set.Questions[0].Expected.Kind);
        Assert.Equal(416161m, set.Questions[0].Expected.Value);
        Assert.True(set.Questions[2].Holdout);
        Assert.Matches("^[0-9a-f]{64}$", sha);
        Assert.Equal(sha, EvalSetLoader.Load(path).Sha256);
    }

    [Theory]
    [InlineData("""{ "version": 1, "questions": [] }""", "no questions")]
    [InlineData("""{ "version": 1, "questions": [ { "id": "a", "tenant": "acme", "category": "c", "question": "q", "expected": { "kind": "number" }, "anchors": [["x"]] } ] }""", "value")]
    [InlineData("""{ "version": 1, "questions": [ { "id": "a", "tenant": "acme", "category": "c", "question": "q", "expected": { "kind": "text" }, "anchors": [["x"]] } ] }""", "text")]
    [InlineData("""{ "version": 1, "questions": [ { "id": "a", "tenant": "acme", "category": "c", "question": "q", "expected": { "kind": "text", "text": "t" }, "anchors": [] } ] }""", "anchor")]
    [InlineData("""{ "version": 1, "questions": [ { "id": "a", "tenant": "acme", "category": "c", "question": "q", "expected": { "kind": "refusal" }, "anchors": [["x"]] } ] }""", "refusal")]
    [InlineData("""{ "version": 1, "questions": [ { "id": "a", "tenant": "acme", "category": "c", "question": "q", "expected": { "kind": "number", "value": 1, "unit": "furlongs" }, "anchors": [["x"]] } ] }""", "unit")]
    [InlineData("""{ "version": 1, "questions": [ { "id": "a", "tenant": "acme", "category": "c", "question": "q", "expected": { "kind": "refusal" }, "anchors": [] }, { "id": "a", "tenant": "acme", "category": "c", "question": "q", "expected": { "kind": "refusal" }, "anchors": [] } ] }""", "duplicate")]
    public void Rejects_invalid_sets_naming_the_problem(string json, string expectedFragment)
    {
        var error = Assert.Throws<InvalidOperationException>(() => EvalSetLoader.Load(Write(json)));

        Assert.Contains(expectedFragment, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
