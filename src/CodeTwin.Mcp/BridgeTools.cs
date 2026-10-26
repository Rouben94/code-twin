using System.Text.Json;
using CodeTwin.Core;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CodeTwin.Mcp;

/// <summary>Exposes the CodeTwin tools plus <c>initialize</c> and routes calls to the bound shell.</summary>
internal sealed class BridgeTools(ShellBridge bridge)
{
    public const string InitializeToolName = "initialize";

    public const string Instructions =
        "Call initialize first with the absolute path of the repository, folder, .sln, .tsproj or .plcproj you work on "
        + "(the current working directory if the user named none). If the session has several repositories or folders, "
        + "pass the others in additionalPaths. "
        + "It finds the TwinCAT solution and connects to the TwinCAT XAE Shell that has it open, or starts one with it. "
        + "If no path has a TwinCAT solution, it connects to the only shell with a solution open. "
        + "Then use the plc_* tools; they act on that solution and fail until initialize succeeded.";

    private static readonly JsonSerializerOptions Json = McpJsonUtilities.DefaultOptions;

    private readonly IList<Tool> tools = CreateTools();

    public ListToolsResult List() => new() { Tools = tools };

    public async ValueTask<CallToolResult> CallAsync(CallToolRequestParams request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(request.Name, InitializeToolName, StringComparison.Ordinal))
            {
                return await InitializeAsync(request.Arguments, cancellationToken).ConfigureAwait(false);
            }

            JsonElement result = await bridge.CallToolAsync(request.Name, request.Arguments, cancellationToken).ConfigureAwait(false);
            return result.Deserialize<CallToolResult>(Json) ?? Error("The shell returned an empty result.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException or HttpRequestException)
        {
            return Error(exception.Message);
        }
    }

    private async Task<CallToolResult> InitializeAsync(IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        if (arguments is null
            || !arguments.TryGetValue("path", out JsonElement path)
            || !IsNonEmptyString(path))
        {
            return Error("The 'path' argument is required.");
        }

        List<string> paths = [path.GetString()!];
        if (arguments.TryGetValue("additionalPaths", out JsonElement additional) && additional.ValueKind != JsonValueKind.Null)
        {
            if (additional.ValueKind != JsonValueKind.Array || !additional.EnumerateArray().All(IsNonEmptyString))
            {
                return Error("'additionalPaths' must be an array of absolute paths.");
            }

            paths.AddRange(additional.EnumerateArray().Select(item => item.GetString()!));
        }

        InitializeResult result = await bridge.InitializeAsync(paths, cancellationToken).ConfigureAwait(false);
        string text = (result.Started, result.FoundInPaths) switch
        {
            (true, _) => $"Started TwinCAT XAE Shell (pid {result.Pid}) with '{result.Solution}'.",
            (false, true) => $"Connected to the running TwinCAT XAE Shell (pid {result.Pid}) that has '{result.Solution}' open.",
            (false, false) => $"No TwinCAT solution found in the given paths. Connected to the only running TwinCAT XAE Shell (pid {result.Pid}), "
                + $"which has '{result.Solution}' open. If that is not the project of this task, call initialize again with the path "
                + "of the repository that contains it.",
        };
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(result, Json),
        };
    }

    private static bool IsNonEmptyString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString());

    private static CallToolResult Error(string message) => new()
    {
        Content = [new TextContentBlock { Text = message }],
        IsError = true,
    };

    private static List<Tool> CreateTools()
    {
        var initialize = new Tool
        {
            Name = InitializeToolName,
            Description = "Connect this session to the TwinCAT project at an absolute path (repository, folder, .sln, .tsproj or .plcproj). "
                + "In a session with several repositories, pass the others in additionalPaths; the one TwinCAT solution across all paths is used. "
                + "Finds the TwinCAT solution, uses the running TwinCAT XAE Shell that has it open, or starts TcXaeShell with it "
                + "(this can take a few minutes). If no path has a TwinCAT solution, connects to the only shell that has a solution open. "
                + "Call it before the plc_* tools (they fail until it succeeded) and whenever you switch projects.",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Absolute path of the repository, folder, .sln, .tsproj or .plcproj." },
                    additionalPaths = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Absolute paths of the other repositories or folders of this session, if there are several.",
                    },
                },
                required = new[] { "path" },
            }),
        };

        List<Tool> catalog = JsonSerializer.SerializeToElement(McpToolCatalog.GetTools(includeWriteTools: true))
            .Deserialize<List<Tool>>(Json) ?? [];
        return [initialize, .. catalog];
    }
}
