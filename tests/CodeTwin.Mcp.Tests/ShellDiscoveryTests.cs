using CodeTwin.Mcp;
using Xunit;

namespace CodeTwin.Mcp.Tests;

public sealed class ShellDiscoveryTests
{
    [Theory]
    [InlineData("http://127.0.0.1:52000/mcp/", true)]
    [InlineData("http://localhost:52000/mcp", true)]
    [InlineData("https://127.0.0.1:52000/mcp/", false)]
    [InlineData("http://192.168.1.10:52000/mcp/", false)]
    [InlineData("http://evil.example/mcp/", false)]
    [InlineData("http://127.0.0.1:52000/other", false)]
    public void AcceptsOnlyTheLoopbackMcpEndpoint(string endpoint, bool expected)
    {
        Assert.Equal(expected, ShellDiscovery.IsLocalEndpoint(new Uri(endpoint)));
    }
}
