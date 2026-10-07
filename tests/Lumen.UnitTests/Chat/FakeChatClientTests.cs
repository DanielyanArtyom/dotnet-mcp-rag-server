namespace Lumen.UnitTests.Chat;

public class FakeChatClientTests
{
    [Fact]
    public async Task Answers_citing_the_first_source_when_sources_are_present()
    {
        var response = await new FakeChatClient().GetResponseAsync(
            [new ChatMessage(ChatRole.System, "sys"), new ChatMessage(ChatRole.User, "<sources>\n<source id=\"1\">x</source>\n</sources>")]);

        Assert.Contains("[1]", response.Text);
        Assert.Equal(ChatConstants.FakeModelName, response.ModelId);
        Assert.True(response.Usage!.InputTokenCount > 0);
    }

    [Fact]
    public async Task Refuses_when_no_sources_are_present()
    {
        var response = await new FakeChatClient().GetResponseAsync([new ChatMessage(ChatRole.User, "Question only")]);

        Assert.Equal(PromptConstants.RefusalSentence, response.Text);
    }
}
