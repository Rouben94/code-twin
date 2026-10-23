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
    private readonly string repository = Directory.CreateTempSubdirectory("codetwin-repo-").FullName;
    private readonly HttpClient http = new();
    private string? openSolution;

    public void Dispose()
    {
        http.Dispose();
        Directory.Delete(instances, recursive: true);
        Directory.Delete(repository, recursive: true);
    }

    [Fact]
    public async Task ConnectsToTheShellThatHasTheRepositoryOpenAndReconnectsAfterARestart()
    {
        openSolution = CreateTwinCatSolution(repository);
        using ShellBridge bridge = CreateBridge();
        var tools = new BridgeTools(bridge);

        using (StartShell("first-token"))
        {
            CallToolResult initialize = await InitializeAsync(tools, repository);
            Assert.NotEqual(true, initialize.IsError);
            Assert.Equal(openSolution, initialize.StructuredContent?.GetProperty("solution").GetString());
            Assert.NotEqual(true, (await CallReadMainAsync(tools)).IsError);
        }

        using InProcessMcpServer second = StartShell("second-token");
        CallToolResult afterRestart = await CallReadMainAsync(tools);

        Assert.NotEqual(true, afterRestart.IsError);
        Assert.Equal("x := 1;", afterRestart.StructuredContent?.GetProperty("Implementation").GetString());
    }

    [Fact]
    public async Task UsesTheOnlyOpenShellWhenThePathHasNoTwinCatSolution()
    {
        openSolution = Path.Combine(instances, "Elsewhere.sln");
        using ShellBridge bridge = CreateBridge();
        var tools = new BridgeTools(bridge);
        using InProcessMcpServer shell = StartShell("token");

        CallToolResult initialize = await InitializeAsync(tools, repository);

        Assert.NotEqual(true, initialize.IsError);
        Assert.Equal(openSolution, initialize.StructuredContent?.GetProperty("solution").GetString());
        Assert.NotEqual(true, (await CallReadMainAsync(tools)).IsError);
    }

    [Fact]
    public async Task RequiresInitializeBeforeTheShellTools()
    {
        openSolution = Path.Combine(instances, "Elsewhere.sln");
        using ShellBridge bridge = CreateBridge();
        using InProcessMcpServer shell = StartShell("token");

        CallToolResult result = await CallReadMainAsync(new BridgeTools(bridge));

        Assert.True(result.IsError);
        Assert.Contains("initialize", Assert.IsType<TextContentBlock>(result.Content[0]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsWhenNeitherThePathNorARunningShellHasASolution()
    {
        using ShellBridge bridge = CreateBridge();

        CallToolResult result = await InitializeAsync(new BridgeTools(bridge), repository);

        Assert.True(result.IsError);
    }

    [Fact]
    public void ListsInitializeAndAllShellTools()
    {
        using ShellBridge bridge = CreateBridge();

        IList<Tool> tools = new BridgeTools(bridge).List().Tools;

        Assert.Equal(BridgeTools.InitializeToolName, tools[0].Name);
        Assert.Equal(McpToolCatalog.GetTools(includeWriteTools: true).Length + 1, tools.Count);
    }

    private ShellBridge CreateBridge() =>
        new(http, instances, (pid, _) => Task.FromResult(pid == Environment.ProcessId ? openSolution : null));

    private InProcessMcpServer StartShell(string token)
    {
        var server = new InProcessMcpServer(new DelegatingPlcAutomation(() => [Main]), token, ReservePort());
        server.Start();
        File.WriteAllText(
            Path.Combine(instances, $"{Environment.ProcessId}.json"),
            JsonSerializer.Serialize(new { pid = Environment.ProcessId, endpoint = server.Endpoint, token }));
        return server;
    }

    private static Task<CallToolResult> InitializeAsync(BridgeTools tools, string path) =>
        tools.CallAsync(
            new CallToolRequestParams
            {
                Name = BridgeTools.InitializeToolName,
                Arguments = new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement(path) },
            },
            CancellationToken.None).AsTask();

    private static Task<CallToolResult> CallReadMainAsync(BridgeTools tools) =>
        tools.CallAsync(
            new CallToolRequestParams
            {
                Name = "plc_read_element",
                Arguments = new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement("Demo/TIPC/MAIN") },
            },
            CancellationToken.None).AsTask();

    private static string CreateTwinCatSolution(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "Machine"));
        File.WriteAllText(Path.Combine(folder, "Machine", "Machine.tsproj"), "<TcSmProject />");
        string solution = Path.Combine(folder, "Machine.sln");
        File.WriteAllText(
            solution,
            "Project(\"{B1E792BE-AA5F-4E3C-8C82-674BF9C0715B}\") = \"Machine\", \"Machine\\Machine.tsproj\", \"{1}\"\r\nEndProject\r\n");
        return solution;
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
