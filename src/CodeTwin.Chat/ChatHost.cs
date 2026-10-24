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
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ExitPlanModeResult>> pendingPlans = new(StringComparer.Ordinal);
    private readonly MonthlyCreditsReader monthlyCredits = new();
    private CopilotSession? session;
    private IDisposable? subscription;
    private string? sessionSolution;
    private string? model;
    private string? reasoningEffort;
    private SessionMode mode = SessionMode.Interactive;
    private int permissionCounter;

    public Task StartAsync(CancellationToken cancellationToken) => client.StartAsync(cancellationToken);

    public async Task PublishModelsAsync(CancellationToken cancellationToken)
    {
        IList<ModelInfo> models = await client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
        channel.Send(new
        {
            type = "models",
            models = models
                .Where(info => info.Policy?.State is null or "enabled")
                .Select(info => new
                {
                    id = info.Id,
                    name = info.Name,
                    multiplier = info.Billing?.Multiplier,
                    contextWindow = info.Capabilities?.Limits?.MaxContextWindowTokens,
                    efforts = info.Capabilities?.Supports?.ReasoningEffort == true ? info.SupportedReasoningEfforts : null,
                    defaultEffort = info.DefaultReasoningEffort,
                }),
        });
    }

#pragma warning disable GHCP001 // The account quota RPC is the only SDK source for the premium request allowance.
    public async Task PublishQuotaAsync(CancellationToken cancellationToken)
    {
        AccountGetQuotaResult result = await client.Rpc.Account.GetQuotaAsync(null, null, cancellationToken).ConfigureAwait(false);
        KeyValuePair<string, AccountQuotaSnapshot> premium = result.QuotaSnapshots
            .FirstOrDefault(snapshot => snapshot.Key.Contains("premium", StringComparison.OrdinalIgnoreCase));
        if (premium.Value is { } quota)
        {
            channel.Send(new
            {
                type = "quota",
                used = quota.UsedRequests,
                entitlement = quota.EntitlementRequests,
                remainingPercentage = quota.RemainingPercentage,
                unlimited = quota.IsUnlimitedEntitlement,
                resetDate = quota.ResetDate,
            });
        }
    }
#pragma warning restore GHCP001

#pragma warning disable GHCP001 // The account auth RPC is the only SDK source for the Copilot plan and login.
    public async Task PublishAccountAsync(CancellationToken cancellationToken)
    {
        AuthInfoUser? user = await ReadSignedInUserAsync(cancellationToken).ConfigureAwait(false);
        MonthlyCredits? monthly = await monthlyCredits.ReadAsync(user?.Login, cancellationToken).ConfigureAwait(false);
        string? plan = user?.CopilotUser?.CopilotPlan ?? monthly?.Plan;
        if (plan is null && monthly is null)
        {
            return;
        }

        channel.Send(new
        {
            type = "account",
            plan,
            resetDate = user?.CopilotUser?.QuotaResetDateUtc ?? monthly?.ResetDateUtc,
            creditsUsed = monthly?.CreditsUsed,
        });
    }

    // The auth RPC fails to deserialize environment-token auth (missing 'token'), so it is optional.
    private async Task<AuthInfoUser?> ReadSignedInUserAsync(CancellationToken cancellationToken)
    {
        try
        {
            AccountGetCurrentAuthResult result = await client.Rpc.Account.GetCurrentAuthAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.AuthInfo as AuthInfoUser;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync($"Reading the Copilot account failed: {exception.Message}").ConfigureAwait(false);
            return null;
        }
    }
#pragma warning restore GHCP001

#pragma warning disable GHCP001 // The usage metrics RPC is the only SDK source for the AI credits a chat consumed.
    private async Task PublishChatCreditsAsync(CopilotSession target, CancellationToken cancellationToken)
    {
        UsageGetMetricsResult metrics = await target.Rpc.Usage.GetMetricsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (metrics.TotalNanoAiu is { } nanoAiu)
        {
            channel.Send(new { type = "chatCredits", credits = nanoAiu / 1e9 });
        }
    }
#pragma warning restore GHCP001

    public async Task SetModelAsync(string? id, string? effort, CancellationToken cancellationToken)
    {
        model = string.IsNullOrEmpty(id) ? null : id;
        reasoningEffort = string.IsNullOrEmpty(effort) ? null : effort;
        if (session is not null && model is not null)
        {
            await session.SetModelAsync(model, new SetModelOptions { ReasoningEffort = reasoningEffort }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SetModeAsync(string? name, CancellationToken cancellationToken)
    {
        mode = ParseMode(name);
        if (session is not null)
        {
            await ApplyModeAsync(session, cancellationToken).ConfigureAwait(false);
        }
    }

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

    public void ResolvePlan(string id, bool approved, string? action, string? feedback)
    {
        if (pendingPlans.TryRemove(id, out TaskCompletionSource<ExitPlanModeResult>? decision))
        {
            decision.TrySetResult(new ExitPlanModeResult { Approved = approved, SelectedAction = action, Feedback = feedback });
        }
    }

    public async ValueTask DisposeAsync()
    {
        RejectPendingPermissions();
        await CloseSessionAsync().ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);
        monthlyCredits.Dispose();
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
                Model = model,
                ReasoningEffort = reasoningEffort,
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
                OnExitPlanModeRequest = DecidePlanAsync,
            },
            cancellationToken).ConfigureAwait(false);

        sessionSolution = solution;
        subscription = session.On<SessionEvent>(Forward);
        channel.Send(new { type = "session", solution });
        if (mode != SessionMode.Interactive)
        {
            await ApplyModeAsync(session, cancellationToken).ConfigureAwait(false);
        }
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
            case SessionModelChangeEvent change:
                channel.Send(new { type = "model", id = change.Data.NewModel, effort = change.Data.ReasoningEffort });
                break;
            case SessionModeChangedEvent change:
                mode = ParseMode(change.Data.NewMode.Value);
                channel.Send(new { type = "mode", mode = change.Data.NewMode.Value });
                break;
            case SessionUsageInfoEvent usage:
                channel.Send(new { type = "contextWindow", current = usage.Data.CurrentTokens, limit = usage.Data.TokenLimit });
                break;
            case AssistantUsageEvent usage:
                channel.Send(new
                {
                    type = "usage",
                    model = usage.Data.Model,
                    input = usage.Data.InputTokens,
                    output = usage.Data.OutputTokens,
                    cacheRead = usage.Data.CacheReadTokens,
                });
                break;
            case SessionIdleEvent:
                channel.Send(new { type = "idle" });
                _ = RefreshUsageAsync(session);
                break;
        }
    }

    private async Task RefreshUsageAsync(CopilotSession? target)
    {
        try
        {
            if (target is not null)
            {
                await PublishChatCreditsAsync(target, CancellationToken.None).ConfigureAwait(false);
            }

            await PublishQuotaAsync(CancellationToken.None).ConfigureAwait(false);
            await PublishAccountAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Reading the Copilot usage failed: {exception.Message}").ConfigureAwait(false);
        }
    }

    private static SessionMode ParseMode(string? name) => name switch
    {
        "plan" => SessionMode.Plan,
        "autopilot" => SessionMode.Autopilot,
        _ => SessionMode.Interactive,
    };

    // MessageOptions.AgentMode does not switch the session; the experimental mode RPC is the only way into plan or autopilot mode.
#pragma warning disable GHCP001
    private Task ApplyModeAsync(CopilotSession target, CancellationToken cancellationToken) =>
        target.Rpc.Mode.SetAsync(mode, cancellationToken: cancellationToken);
#pragma warning restore GHCP001

    private async Task<ExitPlanModeResult> DecidePlanAsync(ExitPlanModeRequest request, ExitPlanModeInvocation invocation)
    {
        string id = Interlocked.Increment(ref permissionCounter).ToString(CultureInfo.InvariantCulture);
        TaskCompletionSource<ExitPlanModeResult> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingPlans[id] = decision;
        channel.Send(new
        {
            type = "plan",
            id,
            summary = request.Summary,
            plan = request.PlanContent,
            actions = request.Actions,
            recommended = request.RecommendedAction,
        });
        return await decision.Task.ConfigureAwait(false);
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

        foreach (string id in pendingPlans.Keys)
        {
            ResolvePlan(id, approved: false, null, null);
        }
    }
}
