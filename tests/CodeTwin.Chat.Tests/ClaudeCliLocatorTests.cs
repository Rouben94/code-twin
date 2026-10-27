using CodeTwin.Chat;
using Xunit;

namespace CodeTwin.Chat.Tests;

public sealed class ClaudeCliLocatorTests
{
    [Fact]
    public void PrefersTheConfiguredPath()
    {
        Assert.Equal(@"D:\cli\claude.exe", ClaudeCliLocator.Locate(@"D:\cli\claude.exe", @"C:\a", @"C:\Users\me", _ => true));
    }

    [Fact]
    public void RejectsAMissingConfiguredPath()
    {
        Assert.Null(ClaudeCliLocator.Locate(@"D:\cli\claude.exe", @"C:\a", @"C:\Users\me", path => !path.StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void SearchesThePathBeforeTheNativeInstallFolder()
    {
        Assert.Equal(@"C:\b\claude.exe", ClaudeCliLocator.Locate(null, @"C:\a;""C:\b""", @"C:\Users\me", path => !path.StartsWith(@"C:\a", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void FallsBackToTheNativeInstallFolder()
    {
        string? located = ClaudeCliLocator.Locate(null, @"C:\a", @"C:\Users\me", path => path.StartsWith(@"C:\Users", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(@"C:\Users\me\.local\bin\claude.exe", located);
    }

    [Fact]
    public void ReturnsNullWhenNotFound()
    {
        Assert.Null(ClaudeCliLocator.Locate(null, @"C:\a", @"C:\Users\me", _ => false));
    }
}
