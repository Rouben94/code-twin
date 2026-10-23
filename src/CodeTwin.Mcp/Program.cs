using System.Reflection;
using CodeTwin.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
using var bridge = new ShellBridge(http, ShellDiscovery.DefaultDirectory);
var tools = new BridgeTools(bridge);

var options = new McpServerOptions
{
    ServerInfo = new Implementation
    {
        Name = "code-twin-mcp",
        Version = typeof(BridgeTools).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0",
    },
    ServerInstructions = BridgeTools.Instructions,
    Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
    Handlers = new McpServerHandlers
    {
        ListToolsHandler = (_, _) => ValueTask.FromResult(tools.List()),
        CallToolHandler = (request, cancellationToken) => tools.CallAsync(request.Params, cancellationToken),
    },
};

await using McpServer server = McpServer.Create(new StdioServerTransport(options), options);
await server.RunAsync();
