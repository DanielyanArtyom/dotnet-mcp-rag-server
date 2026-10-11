namespace Lumen.Eval.Agents;

public static class StreamJsonParser
{
    public static AgentTranscript Parse(IEnumerable<string> lines, bool requireResult = true)
    {
        var transcript = new AgentTranscript();
        var sawResult = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{')
            {
                continue;
            }

            using var json = TryParse(line);
            if (json is null)
            {
                continue;
            }

            var root = json.RootElement;
            switch (Str(root, "type"))
            {
                case "system" when Str(root, "subtype") == "init":
                    transcript.Model = Str(root, "model");
                    break;
                case "assistant":
                    foreach (var block in Content(root).Where(b => Str(b, "type") == "tool_use"))
                    {
                        transcript.ToolCalls.Add(Str(block, "name") ?? string.Empty);
                    }
                    break;
                case "user":
                    foreach (var block in Content(root).Where(b => Str(b, "type") == "tool_result"))
                    {
                        transcript.ToolResults.Add(ToolResultText(block));
                    }
                    break;
                case "result":
                    sawResult = true;
                    transcript.Answer = Str(root, "result") ?? string.Empty;
                    transcript.IsError = root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True;
                    transcript.Turns = Int(root, "num_turns");
                    transcript.DurationMs = Number(root, "duration_ms") is { } ms && ms.TryGetInt64(out var duration) ? duration : 0;
                    transcript.CostUsd = Number(root, "total_cost_usd") is { } cost && cost.TryGetDecimal(out var usd) ? usd : null;
                    if (root.TryGetProperty("usage", out var usage))
                    {
                        transcript.InputTokens = Int(usage, "input_tokens") + Int(usage, "cache_creation_input_tokens") + Int(usage, "cache_read_input_tokens");
                        transcript.OutputTokens = Int(usage, "output_tokens");
                    }

                    if (root.TryGetProperty("permission_denials", out var denials) && denials.ValueKind == JsonValueKind.Array)
                    {
                        transcript.PermissionDenials = denials.EnumerateArray().Select(d => Str(d, "tool_name") ?? "(unknown tool)").ToList();
                    }
                    break;
            }
        }

        return sawResult || !requireResult ? transcript : throw new InvalidOperationException("The claude CLI output contained no result event.");
    }

    private static JsonDocument? TryParse(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<JsonElement> Content(JsonElement root) =>
        root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
        && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray().ToList()
            : [];

    private static string ToolResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join("\n", content.EnumerateArray().Where(c => Str(c, "type") == "text").Select(c => Str(c, "text"))),
            _ => string.Empty
        };
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Int(JsonElement element, string name) =>
        Number(element, name) is { } value && value.TryGetInt32(out var number) ? number : 0;

    private static JsonElement? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value : null;
}
