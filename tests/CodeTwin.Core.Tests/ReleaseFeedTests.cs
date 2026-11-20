using System;
using CodeTwin.Core;
using Xunit;

namespace CodeTwin.Core.Tests;

public sealed class ReleaseFeedTests
{
    private const string Release = """
        {
          "tag_name": "v0.2.0",
          "html_url": "https://github.com/Rouben94/code-twin/releases/tag/v0.2.0",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.test/notes.txt" },
            { "name": "CodeTwin-Setup-0.2.0.exe", "browser_download_url": "https://example.test/CodeTwin-Setup-0.2.0.exe" }
          ]
        }
        """;

    [Fact]
    public void FindsNewerReleaseWithSetup()
    {
        AvailableUpdate? update = ReleaseFeed.FindUpdate(Release, new Version(0, 1, 2));

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 2, 0), update.Version);
        Assert.Equal("https://github.com/Rouben94/code-twin/releases/tag/v0.2.0", update.ReleaseUrl);
        Assert.Equal("https://example.test/CodeTwin-Setup-0.2.0.exe", update.SetupUrl);
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.3.0")]
    public void IgnoresReleaseThatIsNotNewer(string installed) =>
        Assert.Null(ReleaseFeed.FindUpdate(Release, Version.Parse(installed)));

    [Fact]
    public void IgnoresReleaseWithoutSetup() =>
        Assert.Null(ReleaseFeed.FindUpdate(Release.Replace("CodeTwin-Setup-0.2.0.exe\"", "other.exe\"", StringComparison.Ordinal), new Version(0, 1, 0)));
}
