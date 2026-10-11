namespace Lumen.Eval.Tests.Agents;

public class StreamJsonParserTests
{
    private static readonly string[] Transcript =
    [
        """{"type":"system","subtype":"init","model":"claude-sonnet-5-5","tools":["mcp__lumen__search_filings"]}""",
        """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"mcp__lumen__search_filings","input":{"query":"net sales"}}]}}""",
        """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":[{"type":"text","text":"## [1] Apple 10-K"}]}]}}""",
        """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t2","name":"mcp__lumen__list_filings","input":{}}]}}""",
        """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t2","content":"- Apple 10-K — filed 2025-09-27"}]}}""",
        """{"type":"result","subtype":"success","is_error":false,"result":"$416,161 million [1]","num_turns":3,"duration_ms":4200,"total_cost_usd":0.0123,"usage":{"input_tokens":100,"output_tokens":20,"cache_creation_input_tokens":50,"cache_read_input_tokens":30}}"""
    ];

    [Fact]
    public void Reads_answer_model_tools_results_usage_and_cost()
    {
        var t = StreamJsonParser.Parse(Transcript);

        Assert.Equal("$416,161 million [1]", t.Answer);
        Assert.False(t.IsError);
        Assert.Equal("claude-sonnet-5-5", t.Model);
        Assert.Equal(3, t.Turns);
        Assert.Equal(["mcp__lumen__search_filings", "mcp__lumen__list_filings"], t.ToolCalls);
        Assert.Equal(["## [1] Apple 10-K", "- Apple 10-K — filed 2025-09-27"], t.ToolResults);
        Assert.Equal(180, t.InputTokens);
        Assert.Equal(20, t.OutputTokens);
        Assert.Equal(0.0123m, t.CostUsd);
        Assert.Equal(4200, t.DurationMs);
    }

    [Fact]
    public void Ignores_blank_and_non_json_lines()
    {
        var t = StreamJsonParser.Parse(["", "not json", .. Transcript]);

        Assert.Equal("$416,161 million [1]", t.Answer);
    }

    [Fact]
    public void A_stream_without_a_result_event_is_an_error()
    {
        Assert.Throws<InvalidOperationException>(() => StreamJsonParser.Parse(Transcript[..^1]));
    }

    [Fact]
    public void A_tool_result_whose_content_is_null_or_an_object_reads_as_empty_text()
    {
        var t = StreamJsonParser.Parse(
        [
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":null}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t2","content":{"type":"text","text":"x"}}]}}""",
            Transcript[^1]
        ]);

        Assert.Equal(["", ""], t.ToolResults);
    }

    [Fact]
    public void A_truncated_json_line_is_skipped()
    {
        var t = StreamJsonParser.Parse(["""{"type":"assistant","message":{"content":[{"type":"tool_u""", .. Transcript]);

        Assert.Equal("$416,161 million [1]", t.Answer);
    }

    [Fact]
    public void Null_or_non_object_message_usage_and_numbers_read_as_empty_instead_of_throwing()
    {
        var t = StreamJsonParser.Parse(
        [
            """{"type":"assistant","message":null}""",
            """{"type":"user","message":"text"}""",
            """{"type":"result","is_error":false,"result":"ok","num_turns":"3","duration_ms":null,"total_cost_usd":"0.1","usage":null}"""
        ]);

        Assert.Equal("ok", t.Answer);
        Assert.Empty(t.ToolCalls);
        Assert.Empty(t.ToolResults);
        Assert.Equal(0, t.Turns);
        Assert.Equal(0, t.DurationMs);
        Assert.Null(t.CostUsd);
        Assert.Equal(0, t.InputTokens);
    }

    [Fact]
    public void Reads_the_denied_tool_names_from_the_result_event()
    {
        var t = StreamJsonParser.Parse(
        [
            """{"type":"result","is_error":false,"result":"x","num_turns":2,"permission_denials":[{"tool_name":"mcp__lumen__search_filings","tool_use_id":"t1","tool_input":{"query":"q"}}]}"""
        ]);

        Assert.Equal(["mcp__lumen__search_filings"], t.PermissionDenials);
    }

    [Fact]
    public void An_empty_permission_denials_array_means_no_denials()
    {
        Assert.Empty(StreamJsonParser.Parse(Transcript).PermissionDenials);
        Assert.Empty(StreamJsonParser.Parse([Transcript[^1].Replace("}}", "},\"permission_denials\":[]}")]).PermissionDenials);
    }
}
