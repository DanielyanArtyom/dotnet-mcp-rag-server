namespace Lumen.Eval.EvalSets;

public enum ExpectedKind
{
    Number,
    Text,
    Refusal
}

public class ExpectedAnswer
{
    public ExpectedKind Kind { get; set; }
    public decimal? Value { get; set; }
    public string? Unit { get; set; }
    public decimal? Tolerance { get; set; }
    public string? Text { get; set; }
}

public class EvalQuestion
{
    public string Id { get; set; } = default!;
    public string Tenant { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Question { get; set; } = default!;
    public ExpectedAnswer Expected { get; set; } = new();
    public List<List<string>> Anchors { get; set; } = [];
    public bool Holdout { get; set; }
    public string? Note { get; set; }
}

public class EvalSet
{
    public int Version { get; set; }
    public List<EvalQuestion> Questions { get; set; } = [];
}
