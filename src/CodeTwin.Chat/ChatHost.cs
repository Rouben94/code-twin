using System.Collections.Concurrent;
using System.Globalization;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace CodeTwin.Chat;

internal sealed record ChatOptions(string CliPath, string McpUrl, string McpToken, string AgentsDirectory, string SkillsDirectory);

internal sealed class ChatHost(ChatOptions options, ChatChannel channel) : IAsyncDisposable
{
    public const string McpServerName = "codetwin";
    private const string AgentName = "twincat";

    private readonly CopilotClient client = new(new CopilotClientOptions
    {
        Connection = RuntimeConnection.ForStdio(options.CliPath, null),
        UseLoggedInUser = true,
    });

    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> pendingPermissions = new(StringComparer.Ordinal);
    private CopilotSession? session;
    private IDisposable? subscription;
    private string? sessionSolution;
    private int permissionCounter;

    public Task StartAsync(CancellationToken cancellationToken) => client.StartAsync(cancellationToken);

    public async Task SendAsync(string prompt, string? solution, string? activeElement, CancellationToken cancellationToken)
    {
        if (session is null || !string.Equals(sessionSolution, solution, StringComparison.OrdinalIgnoreCase))
        {
            await StartSessionAsync(solution, cancellationToken).ConfigureAwait(false);
        }

        await session!.SendAsync(
            new MessageOptions
            {
                Prompt = ChatContext.CreatePrompt(prompt, solution, activeElement),
                DisplayPrompt = prompt,
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task AbortAsync(CancellationToken cancellationToken)
    {
        RejectPendingPermissions();
        if (session is not null)
        {
            await session.AbortAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task NewChatAsync()
    {
        RejectPendingPermissions();
        await CloseSessionAsync().ConfigureAwait(false);
    }

    public void ResolvePermission(string id, bool approved)
    {
        if (pendingPermissions.TryRemove(id, out TaskCompletionSource<bool>? decision))
        {
            decision.TrySetResult(approved);
        }
    }

    public async ValueTask DisposeAsync()
    {
        RejectPendingPermissions();
        await CloseSessionAsync().ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);
    }

    private async Task StartSessionAsync(string? solution, CancellationToken cancellationToken)
    {
        await CloseSessionAsync().ConfigureAwait(false);

        session = await client.CreateSessionAsync(
            new SessionConfig
            {
                ClientName = "CodeTwin",
                WorkingDirectory = string.IsNullOrEmpty(solution) ? null : Path.GetDirectoryName(solution),
                Agent = AgentName,
                AgentDirectories = [options.AgentsDirectory],
                SkillDirectories = [options.SkillsDirectory],
                McpServers = new Dictionary<string, McpServerConfig>(StringComparer.Ordinal)
                {
                    [McpServerName] = new McpHttpServerConfig
                    {
                        Url = options.McpUrl,
                        Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Authorization"] = $"Bearer {options.McpToken}" },
                        Tools = ["*"],
                    },
                },
                SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Append, Content = ChatContext.CreateSystemMessage(solution) },
                Streaming = true,
                OnPermissionRequest = DecidePermissionAsync,
            },
            cancellationToken).ConfigureAwait(false);

        sessionSolution = solution;
        subscription = session.On<SessionEvent>(Forward);
        channel.Send(new { type = "session", solution });
    }

    private async Task CloseSessionAsync()
    {
        subscription?.Dispose();
        subscription = null;
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            session = null;
        }

        sessionSolution = null;
    }

    private void Forward(SessionEvent sessionEvent)
    {
        switch (sessionEvent)
        {
            case AssistantMessageDeltaEvent delta:
                channel.Send(new { type = "delta", text = delta.Data.DeltaContent });
                break;
            case AssistantMessageEvent message:
                channel.Send(new { type = "message", text = message.Data.Content });
                break;
            case ToolExecutionStartEvent start:
                channel.Send(new { type = "tool", id = start.Data.ToolCallId, name = start.Data.McpToolName ?? start.Data.ToolName, status = "running" });
                break;
            case ToolExecutionCompleteEvent complete:
                channel.Send(new { type = "tool", id = complete.Data.ToolCallId, status = complete.Data.Success ? "done" : "failed" });
                break;
            case SessionErrorEvent error:
                channel.Error(error.Data.Message);
                break;
            case SessionIdleEvent:
                channel.Send(new { type = "idle" });
                break;
        }
    }

#pragma warning disable GHCP001 // PermissionDecision is the SDK's documented opt-in API for custom permission handlers.
    private async Task<PermissionDecision> DecidePermissionAsync(PermissionRequest request, PermissionInvocation invocation)
    {
        if (IsTrusted(request))
        {
            return PermissionDecision.ApproveOnce();
        }

        string id = Interlocked.Increment(ref permissionCounter).ToString(CultureInfo.InvariantCulture);
        TaskCompletionSource<bool> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingPermissions[id] = decision;
        channel.Send(new { type = "permission", id, kind = request.Kind, detail = Describe(request) });

        return await decision.Task.ConfigureAwait(false)
            ? PermissionDecision.ApproveOnce()
            : PermissionDecision.Reject("The user declined this action in the CodeTwin chat.");
    }
#pragma warning restore GHCP001

    private static bool IsTrusted(PermissionRequest request) => request switch
    {
        PermissionRequestMcp mcp => string.Equals(mcp.ServerName, McpServerName, StringComparison.Ordinal),
        PermissionRequestRead => true,
        _ => false,
    };

    private static string Describe(PermissionRequest request) => request switch
    {
        PermissionRequestShell shell => shell.FullCommandText,
        PermissionRequestWrite write => write.FileName,
        PermissionRequestUrl url => url.Url,
        PermissionRequestMcp mcp => $"{mcp.ServerName}/{mcp.ToolName}",
        PermissionRequestCustomTool tool => tool.ToolName,
        PermissionRequestMemory memory => memory.Fact,
        _ => request.Kind,
    };

    private void RejectPendingPermissions()
    {
        foreach (string id in pendingPermissions.Keys)
        {
            ResolvePermission(id, approved: false);
        }
    }
}
