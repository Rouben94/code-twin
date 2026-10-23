using CodeTwin.Chat;
using Xunit;

namespace CodeTwin.Chat.Tests;

public sealed class ChatContextTests
{
    [Fact]
    public void PrefixesThePromptWithSolutionAndActiveElement()
    {
        string prompt = ChatContext.CreatePrompt("Fix this POU", @"C:\repo\Machine.sln", "Machine/Plc/Plc Project/POUs/MAIN");

        Assert.Equal(
            "<context>\r\nSolution: C:\\repo\\Machine.sln\r\nActive element: Machine/Plc/Plc Project/POUs/MAIN\r\n</context>\r\n\r\nFix this POU",
            prompt.ReplaceLineEndings("\r\n"));
    }

    [Fact]
    public void MarksMissingContext()
    {
        string prompt = ChatContext.CreatePrompt("Hi", null, null);

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
