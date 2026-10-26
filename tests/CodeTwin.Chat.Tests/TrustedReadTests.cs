using CodeTwin.Chat;
using Xunit;

namespace CodeTwin.Chat.Tests;

public sealed class TrustedReadTests
{
    private static readonly string?[] Folders = [@"C:\Repos\Machine", @"C:\Ext\chat\skills", null];

    [Theory]
    [InlineData(@"C:\Repos\Machine\PLC\POUs\MAIN.TcPOU")]
    [InlineData(@"C:\Repos\Machine")]
    [InlineData(@"c:\repos\machine\Machine.sln")]
    [InlineData(@"C:\Ext\chat\skills\twincat-naming\SKILL.md")]
    public void TrustsReadsInsideTheTrustedFolders(string path)
    {
        Assert.True(ChatHost.IsTrustedRead(path, Folders));
    }

    [Theory]
    [InlineData(@"C:\Users\me\.ssh\id_ed25519")]
    [InlineData(@"C:\Repos\Machine2\secrets.env")]
    [InlineData(@"C:\Repos\Machine\..\Other\file.txt")]
    [InlineData(@"\\attacker\share\file.txt")]
    [InlineData(@"\\?\UNC\attacker\share\file.txt")]
    [InlineData(@"Machine\file.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void AsksForEverythingElse(string? path)
    {
        Assert.False(ChatHost.IsTrustedRead(path, Folders));
    }

    [Fact]
    public void DoesNotTrustANetworkSolutionFolder()
    {
        Assert.False(ChatHost.IsTrustedRead(@"\\server\share\Machine\MAIN.TcPOU", [@"\\server\share\Machine"]));
    }
}
