using CodeTwin.Chat;
using Xunit;

namespace CodeTwin.Chat.Tests;

public sealed class ChatContextTests
{
    [Fact]
    public void PrefixesThePromptWithSolutionAndActiveElement()
    {
        string prompt = ChatContext.CreatePrompt("Fix this POU", [], @"C:\repo\Machine.sln", "Machine/Plc/Plc Project/POUs/MAIN");

        Assert.Equal(
            "<context>\r\nSolution: C:\\repo\\Machine.sln\r\nActive element: Machine/Plc/Plc Project/POUs/MAIN\r\n</context>\r\n\r\nFix this POU",
            prompt.ReplaceLineEndings("\r\n"));
    }

    [Fact]
    public void ListsTheAttachedFilesInTheContextAndReadsThemBack()
    {
        string[] files = [@"C:\Users\me\Desktop\wiring.png", @"C:\docs\spec.pdf"];

        string prompt = ChatContext.CreatePrompt("Check this", files, null, null);

        Assert.Contains("Attached file: C:\\Users\\me\\Desktop\\wiring.png\r\nAttached file: C:\\docs\\spec.pdf\r\n</context>", prompt.ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
        Assert.Equal(files, ClaudeTranscripts.AttachedFiles(prompt));
        Assert.Equal("Check this", ClaudeTranscripts.StripContext(prompt));
    }

    [Fact]
    public void MarksMissingContext()
    {
        string prompt = ChatContext.CreatePrompt("Hi", [], null, null);

        Assert.Contains("Solution: (none open)", prompt, StringComparison.Ordinal);
        Assert.Contains("Active element: (none)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemMessageNamesTheSolutionAndRulesOutInitialize()
    {
        string message = ChatContext.CreateSystemMessage(@"C:\repo\Machine.sln");

        Assert.Contains(@"C:\repo\Machine.sln", message, StringComparison.Ordinal);
        Assert.Contains("no `initialize` tool", message, StringComparison.Ordinal);
    }
}
