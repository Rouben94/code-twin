using CodeTwin.Chat;
using Xunit;

namespace CodeTwin.Chat.Tests;

public sealed class CopilotCliLocatorTests
{
    [Fact]
    public void PrefersTheConfiguredPath()
    {
        Assert.Equal(@"D:\cli\copilot.exe", CopilotCliLocator.Locate(@"D:\cli\copilot.exe", @"C:\a", _ => true));
    }

    [Fact]
    public void RejectsAMissingConfiguredPath()
    {
        Assert.Null(CopilotCliLocator.Locate(@"D:\cli\copilot.exe", @"C:\a", path => !path.StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void SearchesThePathInOrder()
    {
        string? located = CopilotCliLocator.Locate(null, @"C:\a;""C:\b"";C:\c", path => !path.StartsWith(@"C:\a", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(@"C:\b\copilot.exe", located);
    }

    [Fact]
    public void ReturnsNullWhenNotFound()
    {
        Assert.Null(CopilotCliLocator.Locate(null, @"C:\a;C:\b", _ => false));
    }
}
