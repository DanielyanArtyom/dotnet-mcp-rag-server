namespace Lumen.Eval.Tests.Agents;

public class ClaudeCliAgentClientTests
{
    private const string ErrorResult = """{"type":"result","subtype":"error_during_execution","is_error":true,"result":"MCP server lumen failed","num_turns":1}""";
    private const string SuccessResult = """{"type":"result","subtype":"success","is_error":false,"result":"416,161 [1]","num_turns":2}""";

    [Fact]
    public void A_non_zero_exit_with_a_result_event_is_an_error_carrying_the_result_text_and_stderr()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ClaudeCliAgentClient.ReadOutput(1, ErrorResult, "boom on stderr", "secret-token"));

        Assert.Contains("exit code 1", ex.Message);
        Assert.Contains("MCP server lumen failed", ex.Message);
        Assert.Contains("boom on stderr", ex.Message);
    }

    [Fact]
    public void A_non_zero_exit_without_any_output_reports_the_tail_of_stderr()
    {
        var stderr = new string('x', 1000) + "the real cause";

        var ex = Assert.Throws<InvalidOperationException>(() => ClaudeCliAgentClient.ReadOutput(2, string.Empty, stderr, "eyJ.token.sig"));

        Assert.Contains("the real cause", ex.Message);
        Assert.DoesNotContain(new string('x', 600), ex.Message);
    }

    [Fact]
    public void The_bearer_token_never_appears_in_an_error_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ClaudeCliAgentClient.ReadOutput(1, string.Empty, "Authorization: Bearer secret-token rejected", "secret-token"));

        Assert.DoesNotContain("secret-token", ex.Message);
    }

    [Fact]
    public void A_zero_exit_returns_the_parsed_transcript()
    {
        var transcript = ClaudeCliAgentClient.ReadOutput(0, SuccessResult, string.Empty, "t");

        Assert.Equal("416,161 [1]", transcript.Answer);
    }

    [Fact]
    public void A_timed_out_run_keeps_the_tool_results_streamed_before_the_kill()
    {
        var stdout = string.Join("\n",
            """{"type":"system","subtype":"init","model":"claude-x"}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"documentId: 11111111-1111-1111-1111-111111111111"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_u""");

        var transcript = ClaudeCliAgentClient.ReadTimedOutOutput(stdout);

        Assert.True(transcript.TimedOut);
        Assert.Equal(["documentId: 11111111-1111-1111-1111-111111111111"], transcript.ToolResults);
        Assert.Equal("claude-x", transcript.Model);
    }

    [Fact]
    public void A_timed_out_run_with_no_output_is_an_empty_timed_out_transcript()
    {
        var transcript = ClaudeCliAgentClient.ReadTimedOutOutput(string.Empty);

        Assert.True(transcript.TimedOut);
        Assert.Empty(transcript.ToolResults);
    }
}
