namespace Lumen.Eval.EvalSets;

public static class EvalSetLoader
{
    public static (EvalSet Set, string Sha256) Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var set = JsonSerializer.Deserialize<EvalSet>(bytes, JsonDefaults.Options)
            ?? throw new InvalidOperationException($"Eval set '{path}' is empty.");

        Validate(set);

        return (set, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public static void Validate(EvalSet set)
    {
        var problems = new List<string>();

        if (set.Questions.Count == 0)
        {
            problems.Add("The eval set has no questions.");
        }

        foreach (var duplicate in set.Questions.GroupBy(q => q.Id).Where(g => g.Count() > 1))
        {
            problems.Add($"Duplicate question id '{duplicate.Key}'.");
        }

        foreach (var q in set.Questions)
        {
            var id = string.IsNullOrWhiteSpace(q.Id) ? "(no id)" : q.Id;

            if (string.IsNullOrWhiteSpace(q.Id) || string.IsNullOrWhiteSpace(q.Tenant)
                || string.IsNullOrWhiteSpace(q.Category) || string.IsNullOrWhiteSpace(q.Question))
            {
                problems.Add($"{id}: id, tenant, category and question are required.");
            }

            var hasAnchors = q.Anchors.Any(g => g.Count > 0 && g.All(a => !string.IsNullOrWhiteSpace(a)));

            switch (q.Expected.Kind)
            {
                case ExpectedKind.Number:
                    if (q.Expected.Value is null)
                    {
                        problems.Add($"{id}: a number answer needs expected.value.");
                    }

                    if (!NumberScorer.KnownUnits.Contains(q.Expected.Unit ?? string.Empty))
                    {
                        problems.Add($"{id}: unknown expected.unit '{q.Expected.Unit}' (use thousands, millions, billions, percent or omit it).");
                    }

                    break;
                case ExpectedKind.Text when string.IsNullOrWhiteSpace(q.Expected.Text):
                    problems.Add($"{id}: a text answer needs expected.text.");
                    break;
                case ExpectedKind.Refusal when q.Anchors.Count > 0:
                    problems.Add($"{id}: a refusal question must have no anchors.");
                    break;
            }

            if (q.Expected.Kind != ExpectedKind.Refusal && !hasAnchors)
            {
                problems.Add($"{id}: an answerable question needs at least one non-empty anchor group.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Invalid eval set:\n- " + string.Join("\n- ", problems));
        }
    }
}
