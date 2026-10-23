using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CodeTwin.Core;
using ModelContextProtocol.Protocol;
using Xunit;

namespace CodeTwin.Mcp.Tests;

public sealed class ShellBridgeTests : IDisposable
{
    private static readonly PlcElement Main = new("Demo", "MAIN", "Demo/TIPC/MAIN", "POU", "VAR\nEND_VAR", "x := 1;");

    private readonly string instances = Directory.CreateTempSubdirectory("codetwin-instances-").FullName;

    public void Dispose() => Directory.Delete(instances, recursive: true);

    [Fact]
    public async Task ForwardsToTheOnlyRunningShellAndReconnectsAfterARestart()
    {
        using var http = new HttpClient();
        using var bridge = new ShellBridge(http, instances);
        var tools = new BridgeTools(bridge);

        using (InProcessMcpServer first = StartShell("first-token"))
        {
            CallToolResult result = await CallReadMainAsync(tools);
            Assert.NotEqual(true, result.IsError);
        }

        using InProcessMcpServer second = StartShell("second-token");
        CallToolResult afterRestart = await CallReadMainAsync(tools);

        Assert.NotEqual(true, afterRestart.IsError);
        Assert.Equal("x := 1;", afterRestart.StructuredContent?.GetProperty("Implementation").GetString());
    }

    [Fact]
    public async Task AsksForInitializeWhenNoShellIsRunning()
    {
        using var http = new HttpClient();
        using var bridge = new ShellBridge(http, instances);

        CallToolResult result = await new BridgeTools(bridge).CallAsync(ReadMain(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("initialize", Assert.IsType<TextContentBlock>(result.Content[0]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ListsInitializeAndAllShellTools()
    {
        using var http = new HttpClient();
        using var bridge = new ShellBridge(http, instances);

        IList<Tool> tools = new BridgeTools(bridge).List().Tools;

        Assert.Equal(BridgeTools.InitializeToolName, tools[0].Name);
        Assert.Equal(McpToolCatalog.GetTools(includeWriteTools: true).Length + 1, tools.Count);
    }

    private InProcessMcpServer StartShell(string token)
    {
        var server = new InProcessMcpServer(new DelegatingPlcAutomation(() => [Main]), token, ReservePort());
        server.Start();
        File.WriteAllText(
            Path.Combine(instances, $"{Environment.ProcessId}.json"),
            JsonSerializer.Serialize(new { pid = Environment.ProcessId, endpoint = server.Endpoint, token }));
        return server;
    }

    private static Task<CallToolResult> CallReadMainAsync(BridgeTools tools) =>
        tools.CallAsync(ReadMain(), CancellationToken.None).AsTask();

    private static CallToolRequestParams ReadMain() => new()
    {
        Name = "plc_read_element",
        Arguments = new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement("Demo/TIPC/MAIN") },
    };

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
