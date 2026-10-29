using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeTwin.Chat;

/// <summary>
/// Runs the chat on the user's own Claude Code (claude.exe), signed in with their own Claude subscription or API key.
/// It loads the same agent and skills as the Copilot chat (the chat folder is laid out as a Claude Code plugin)
/// and sends the same JSON lines to the chat UI as <see cref="ChatHost"/>.
/// </summary>
internal sealed record PlanLimit(string Label, double Percent, DateTimeOffset? ResetsAt);

internal sealed class ClaudeChatHost(ChatOptions options, ChatChannel channel) : IChatHost
{
    // The chat folder's .claude-plugin/plugin.json names the plugin "codetwin"; its agent is "twincat".
    private const string AgentName = "codetwin:twincat";
    private const string PlanFeedback = "The user wants to keep planning. Ask what to change or refine the plan.";

    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> pendingPermissions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PlanDecision>> pendingPlans = new(StringComparer.Ordinal);
    private readonly string pluginDirectory = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(options.AgentsDirectory))!;
    private string projectsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
    private ClaudeCli? cli;
    private string? sessionId;
    private string? sessionSolution;
    private bool sessionStored;
    private string? cliEffort;
    private string? model;
    private string? reasoningEffort;
    private string mode = "interactive";
    private volatile bool approveAll;
    private volatile bool busy;
    private bool privateChat;
    private int permissionCounter;
    private string[] attachedFiles = [];
    private string? lastMessageId;
    private long contextTokens;
    private IReadOnlyList<(string Id, string? Resolved)> models = [];

    private sealed record PlanDecision(bool Approved, string? Action, string? Feedback);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string version = await RunAsync("--version", cancellationToken).ConfigureAwait(false);
        await Console.Error.WriteLineAsync($"Using Claude Code {version.Trim()} from {options.CliPath}").ConfigureAwait(false);
    }

    // Claude Code lists the models of the signed-in account in its initialize response; no prompt is sent.
    public async Task PublishModelsAsync(CancellationToken cancellationToken)
    {
        await using ClaudeCli probe = ClaudeCli.Start(options.CliPath, NeutralDirectory(), [.. ClaudeCli.StreamArguments, "--strict-mcp-config"], _ => Task.CompletedTask);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        JsonNode? response = await probe.RequestAsync(new { subtype = "initialize" }, timeout.Token).ConfigureAwait(false);
        List<JsonNode> list = [.. (response?["models"] as JsonArray ?? []).OfType<JsonNode>()];
        models = [.. list.Select(info => (info["value"]?.GetValue<string>() ?? string.Empty, info["resolvedModel"]?.GetValue<string>()))];
        channel.Send(new
        {
            type = "models",
            models = list
                .Select(info => new
                {
                    id = info["value"]?.GetValue<string>(),
                    name = info["displayName"]?.GetValue<string>(),
                    multiplier = (double?)null,
                    contextWindow = (long?)null,
                    efforts = info["supportsEffort"]?.GetValue<bool>() == true ? info["supportedEffortLevels"] : null,
                    defaultEffort = (string?)null,
                }),
        });
    }

    // Claude Code answers /usage itself, without a model call; later model calls update the limits through rate_limit_event.
    public async Task PublishQuotaAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<JsonNode?> report = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ClaudeCli probe = ClaudeCli.Start(
            options.CliPath,
            NeutralDirectory(),
            [.. ClaudeCli.StreamArguments, "--strict-mcp-config", "--no-session-persistence"],
            message =>
            {
                if (message["usage_report"] is JsonNode usage)
                {
                    report.TrySetResult(usage);
                }
                else if (message["type"]?.GetValue<string>() == "result")
                {
                    report.TrySetResult(null);
                }

                return Task.CompletedTask;
            });
        probe.SendUserMessage("/usage");

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        if (UsageLimits(await report.Task.WaitAsync(timeout.Token).ConfigureAwait(false)) is { Count: > 0 } items)
        {
            channel.Send(new { type = "limits", items });
        }
    }

    public async Task<bool> PublishAuthStatusAsync(CancellationToken cancellationToken)
    {
        bool signedIn = false;
        try
        {
            JsonNode? status = JsonNode.Parse(await RunAsync("auth status --json", cancellationToken).ConfigureAwait(false));
            signedIn = status?["loggedIn"]?.GetValue<bool>() == true;
            if (status?["projectsDirectory"]?.GetValue<string>() is { Length: > 0 } directory)
            {
                projectsDirectory = directory;
            }
        }
        catch (JsonException exception)
        {
            await Console.Error.WriteLineAsync($"Reading the Claude Code sign-in status failed: {exception.Message}").ConfigureAwait(false);
        }

        channel.Send(new { type = "auth", signedIn });
        return signedIn;
    }

    // Sign-in stays with Claude Code: it runs `claude auth login` in its own console window, CodeTwin never sees a credential.
    public async Task<bool> SignInAsync(CancellationToken cancellationToken)
    {
        using Process login = Process.Start(new ProcessStartInfo(options.CliPath, "auth login") { UseShellExecute = true })
            ?? throw new InvalidOperationException("Claude sign-in could not be started.");
        await login.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return await PublishAuthStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetModelAsync(string? id, string? effort, CancellationToken cancellationToken)
    {
        model = string.IsNullOrEmpty(id) ? null : id;
        reasoningEffort = string.IsNullOrEmpty(effort) ? null : effort;
        if (cli is not null && model is not null)
        {
            await cli.RequestAsync(new { subtype = "set_model", model }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SetModeAsync(string? name, CancellationToken cancellationToken)
    {
        mode = name is "plan" or "autopilot" ? name : "interactive";
        if (cli is not null)
        {
            await cli.RequestAsync(new { subtype = "set_permission_mode", mode = PermissionMode(mode) }, cancellationToken).ConfigureAwait(false);
        }
    }

    public void SetToolPermissions(string? name) =>
        approveAll = string.Equals(name, "approveAll", StringComparison.Ordinal);

    public async Task SendAsync(string prompt, IReadOnlyList<string> attachments, string? solution, string? activeElement, CancellationToken cancellationToken)
    {
        if (sessionId is null || !string.Equals(sessionSolution, solution, StringComparison.OrdinalIgnoreCase))
        {
            await StartSessionAsync(solution).ConfigureAwait(false);
        }
        else if (cli is null || cli.HasExited || (!busy && sessionStored && cliEffort != reasoningEffort))
        {
            // The effort is fixed per process: a changed effort, or an exited process, continues the stored chat in a new one.
            await CloseProcessAsync().ConfigureAwait(false);
            StartProcess(resume: sessionStored);
        }

        object content = await ClaudeMessageContent.CreateAsync(
            ChatContext.CreatePrompt(prompt, attachments, solution, activeElement), attachments, cancellationToken).ConfigureAwait(false);
        attachedFiles = [.. attachedFiles, .. attachments];
        busy = true;
        cli!.SendUserMessage(content);
    }

    public async Task AbortAsync(CancellationToken cancellationToken)
    {
        RejectPendingPermissions();
        if (cli is not null && !cli.HasExited)
        {
            await cli.RequestAsync(new { subtype = "interrupt" }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task NewChatAsync(bool isPrivate)
    {
        RejectPendingPermissions();
        await CloseSessionAsync().ConfigureAwait(false);
        privateChat = isPrivate;
        approveAll = false;
    }

    public Task PublishSessionsAsync(string? solution, CancellationToken cancellationToken)
    {
        string? folder = SolutionFolder(solution);
        IReadOnlyList<ClaudeTranscript> transcripts = folder is null
            ? []
            : ClaudeTranscripts.List(ClaudeTranscripts.ProjectFolder(projectsDirectory, folder), AgentName);
        channel.Send(new
        {
            type = "sessions",
            items = transcripts.Select(transcript => new
            {
                id = transcript.Id,
                summary = transcript.Summary,
                modified = transcript.Modified,
                current = transcript.Id == sessionId,
            }),
        });
        return Task.CompletedTask;
    }

    public async Task ResumeAsync(string id, string? solution, CancellationToken cancellationToken)
    {
        RejectPendingPermissions();
        await CloseSessionAsync().ConfigureAwait(false);
        privateChat = false;
        approveAll = false;

        sessionId = id;
        sessionSolution = solution;
        sessionStored = true;
        string? folder = SolutionFolder(solution);
        channel.Send(new
        {
            type = "history",
            id,
            items = folder is null ? [] : ClaudeTranscripts.History(ClaudeTranscripts.ProjectFolder(projectsDirectory, folder), id),
        });
        StartProcess(resume: true);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        if (id == sessionId)
        {
            RejectPendingPermissions();
            await CloseSessionAsync().ConfigureAwait(false);
        }

        // Chat ids are GUIDs, so deleting the id in every project folder removes exactly this chat.
        if (Directory.Exists(projectsDirectory))
        {
            foreach (string projectFolder in Directory.EnumerateDirectories(projectsDirectory))
            {
                ClaudeTranscripts.Delete(projectFolder, id);
            }
        }
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
        if (pendingPlans.TryRemove(id, out TaskCompletionSource<PlanDecision>? decision))
        {
            decision.TrySetResult(new PlanDecision(approved, action, feedback));
        }
    }

    public async ValueTask DisposeAsync()
    {
        RejectPendingPermissions();
        await CloseSessionAsync().ConfigureAwait(false);
    }

    private async Task StartSessionAsync(string? solution)
    {
        await CloseSessionAsync().ConfigureAwait(false);
        sessionId = Guid.NewGuid().ToString();
        sessionSolution = solution;
        sessionStored = false;
        StartProcess(resume: false);
        channel.Send(new { type = "session", solution, id = sessionId, @private = privateChat });
    }

    private void StartProcess(bool resume)
    {
        List<string> arguments =
        [
            .. ClaudeCli.StreamArguments,
            "--plugin-dir", pluginDirectory,
            "--agent", AgentName,
            "--mcp-config", McpConfig(),
            "--append-system-prompt", ChatContext.CreateSystemMessage(sessionSolution),
            "--permission-mode", PermissionMode(mode),
            // The chat has no UI for questions; the agent asks in its reply instead.
            "--disallowedTools", "AskUserQuestion",
            resume ? "--resume" : "--session-id", sessionId!,
        ];
        if (privateChat)
        {
            arguments.Add("--no-session-persistence");
        }

        if (model is not null)
        {
            arguments.AddRange(["--model", model]);
        }

        if (reasoningEffort is not null)
        {
            arguments.AddRange(["--effort", reasoningEffort]);
        }

        lastMessageId = null;
        cliEffort = reasoningEffort;
        cli = ClaudeCli.Start(options.CliPath, SolutionFolder(sessionSolution) ?? NeutralDirectory(), arguments, HandleAsync);
    }

    private string McpConfig() => JsonSerializer.Serialize(new
    {
        mcpServers = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [ChatHost.McpServerName] = new
            {
                type = "http",
                url = options.McpUrl,
                headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Authorization"] = $"Bearer {options.McpToken}" },
            },
        },
    });

    private async Task CloseProcessAsync()
    {
        ClaudeCli? running = cli;
        cli = null;
        busy = false;
        if (running is not null)
        {
            await running.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task CloseSessionAsync()
    {
        await CloseProcessAsync().ConfigureAwait(false);
        attachedFiles = [];
        sessionId = null;
        sessionSolution = null;
        sessionStored = false;
    }

    private Task HandleAsync(JsonNode message)
    {
        // Subagents report through the same stream; the chat shows only the main conversation.
        if (message["parent_tool_use_id"] is JsonValue)
        {
            return Task.CompletedTask;
        }

        switch (message["type"]?.GetValue<string>())
        {
            case "stream_event":
                JsonNode? delta = message["event"]?["delta"];
                if (delta?["type"]?.GetValue<string>() == "text_delta")
                {
                    channel.Send(new { type = "delta", text = delta["text"]?.GetValue<string>() });
                }

                break;
            case "assistant":
                ForwardAssistant(message);
                break;
            case "user":
                foreach (JsonNode result in Blocks(message, "tool_result"))
                {
                    channel.Send(new { type = "tool", id = result["tool_use_id"]?.GetValue<string>(), status = result["is_error"]?.GetValue<bool>() == true ? "failed" : "done" });
                }

                break;
            case "system":
                ForwardSystem(message);
                break;
            case "result":
                ForwardResult(message);
                break;
            case "rate_limit_event":
                if (PlanLimits(message) is { Count: > 0 } items)
                {
                    channel.Send(new { type = "limits", items });
                }

                break;
            case "control_request":
                // Answered asynchronously: the user decides while the stream keeps flowing.
                _ = DecideAsync(message);
                break;
        }

        return Task.CompletedTask;
    }

    private void ForwardAssistant(JsonNode message)
    {
        if (message["error"]?.GetValue<string>() == "authentication_failed")
        {
            // Claude Code can report a sign-in whose token has expired; the request tells the truth.
            channel.Send(new { type = "auth", signedIn = false });
        }

        JsonNode? body = message["message"];
        string reply = string.Concat(Blocks(message, "text").Select(block => block["text"]?.GetValue<string>()));
        if (reply.Length > 0)
        {
            channel.Send(new { type = "message", text = reply });
        }

        foreach (JsonNode use in Blocks(message, "tool_use"))
        {
            channel.Send(new { type = "tool", id = use["id"]?.GetValue<string>(), name = ClaudeTranscripts.ToolName(use["name"]?.GetValue<string>()), status = "running" });
        }

        // Claude Code repeats an API message once per content block; count its usage once.
        string? id = body?["id"]?.GetValue<string>();
        JsonNode? usage = body?["usage"];
        if (usage is null || id is null || id == lastMessageId || body?["model"]?.GetValue<string>() == "<synthetic>")
        {
            return;
        }

        lastMessageId = id;

        // Tokens written to the prompt cache are uncached input too; Claude reports them separately.
        long input = Number(usage["input_tokens"]) + Number(usage["cache_creation_input_tokens"]);
        long cacheRead = Number(usage["cache_read_input_tokens"]);
        contextTokens = input + cacheRead;
        channel.Send(new
        {
            type = "usage",
            model = body?["model"]?.GetValue<string>(),
            input,
            output = Number(usage["output_tokens"]),
            cacheRead,
        });
    }

    private void ForwardSystem(JsonNode message)
    {
        switch (message["subtype"]?.GetValue<string>())
        {
            case "init":
                channel.Send(new { type = "model", id = PickerModel(model, message["model"]?.GetValue<string>(), models), effort = reasoningEffort });
                break;
            case "status" when message["permissionMode"]?.GetValue<string>() is { } permissionMode:
                mode = permissionMode switch
                {
                    "plan" => "plan",
                    "acceptEdits" or "auto" or "bypassPermissions" => "autopilot",
                    _ => "interactive",
                };
                channel.Send(new { type = "mode", mode });
                break;
        }
    }

    private void ForwardResult(JsonNode message)
    {
        busy = false;
        sessionStored = !privateChat;
        string? reason = message["terminal_reason"]?.GetValue<string>();
        if (message["is_error"]?.GetValue<bool>() == true && reason?.StartsWith("aborted", StringComparison.Ordinal) != true)
        {
            channel.Error(message["result"]?.GetValue<string>() ?? "Claude Code reported an error.");
        }

        if (message["modelUsage"] is JsonObject models && models.Select(entry => Number(entry.Value?["contextWindow"])).DefaultIfEmpty().Max() is > 0 and var limit)
        {
            channel.Send(new { type = "contextWindow", current = contextTokens, limit });
        }

        channel.Send(new { type = "idle" });
    }

    private async Task DecideAsync(JsonNode message)
    {
        string? requestId = message["request_id"]?.GetValue<string>();
        JsonNode? request = message["request"];
        ClaudeCli? target = cli;
        if (requestId is null || request?["subtype"]?.GetValue<string>() != "can_use_tool" || target is null)
        {
            return;
        }

        string tool = request["tool_name"]?.GetValue<string>() ?? string.Empty;
        JsonNode input = request["input"]?.DeepClone() ?? new JsonObject();
        object allow = new { behavior = "allow", updatedInput = input };
        try
        {
            if (tool == "ExitPlanMode")
            {
                PlanDecision plan = await DecidePlanAsync(input).ConfigureAwait(false);
                target.Respond(requestId, plan.Approved
                    ? allow
                    : new { behavior = "deny", message = string.IsNullOrWhiteSpace(plan.Feedback) ? PlanFeedback : plan.Feedback });
                if (plan.Approved)
                {
                    mode = plan.Action == "autopilot" ? "autopilot" : "interactive";
                    await target.RequestAsync(new { subtype = "set_permission_mode", mode = PermissionMode(mode) }, CancellationToken.None).ConfigureAwait(false);
                }

                return;
            }

            bool approved = approveAll || IsTrusted(tool, input) || await AskAsync(tool, input).ConfigureAwait(false);
            target.Respond(requestId, approved ? allow : new { behavior = "deny", message = "The user declined this action in the CodeTwin chat." });
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // The chat was closed while the user decided.
        }
    }

    private Task<PlanDecision> DecidePlanAsync(JsonNode input)
    {
        string id = Interlocked.Increment(ref permissionCounter).ToString(CultureInfo.InvariantCulture);
        TaskCompletionSource<PlanDecision> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingPlans[id] = decision;
        channel.Send(new
        {
            type = "plan",
            id,
            summary = (string?)null,
            plan = input["plan"]?.GetValue<string>(),
            actions = new[] { "interactive", "autopilot" },
            recommended = "interactive",
        });
        return decision.Task;
    }

    private Task<bool> AskAsync(string tool, JsonNode input)
    {
        string id = Interlocked.Increment(ref permissionCounter).ToString(CultureInfo.InvariantCulture);
        TaskCompletionSource<bool> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingPermissions[id] = decision;
        (string kind, string? detail) = Describe(tool, input);
        channel.Send(new { type = "permission", id, kind, detail });
        return decision.Task;
    }

    private bool IsTrusted(string tool, JsonNode input) => tool switch
    {
        _ when tool.StartsWith($"mcp__{ChatHost.McpServerName}__", StringComparison.Ordinal) => true,
        "Read" or "Glob" or "Grep" => ChatHost.IsTrustedRead(
            Text(input, "file_path") ?? Text(input, "path"),
            [SolutionFolder(sessionSolution), options.AgentsDirectory, options.SkillsDirectory, .. attachedFiles]),
        _ => false,
    };

    internal static (string Kind, string? Detail) Describe(string tool, JsonNode input) => tool switch
    {
        "Bash" or "PowerShell" => ("shell", Text(input, "command")),
        "Write" or "Edit" or "MultiEdit" or "NotebookEdit" => ("write", Text(input, "file_path") ?? Text(input, "notebook_path")),
        "Read" or "Glob" or "Grep" => ("read", Text(input, "file_path") ?? Text(input, "path") ?? Text(input, "pattern")),
        "WebFetch" or "WebSearch" => ("url", Text(input, "url") ?? Text(input, "query")),
        _ when tool.StartsWith("mcp__", StringComparison.Ordinal) => ("mcp", tool["mcp__".Length..].Replace("__", "/", StringComparison.Ordinal)),
        _ => (tool, null),
    };

    /// <summary>
    /// The model picker lists Claude Code's aliases (opus, default, ...), but a running session reports the resolved
    /// model id (claude-opus-5-5). Report the user's choice, or else the first alias that resolves to the running model.
    /// </summary>
    internal static string? PickerModel(string? selected, string? running, IReadOnlyList<(string Id, string? Resolved)> models) =>
        selected
        ?? models.FirstOrDefault(entry => entry.Id == running || (running is not null && entry.Resolved == running)).Id
        ?? running;

    /// <summary>The subscription's usage windows, which Claude Code reports after every model call.</summary>
    internal static IReadOnlyList<PlanLimit> PlanLimits(JsonNode message)
    {
        JsonNode? windows = message["rate_limit_info"]?["unifiedWindows"];
        List<PlanLimit> limits = [];
        foreach ((string key, string label) in (ReadOnlySpan<(string, string)>)[("five_hour", "5-hour limit"), ("seven_day", "Weekly limit")])
        {
            if (windows?[key] is not JsonObject window)
            {
                continue;
            }

            double utilization = window["utilization"] is JsonValue value && value.TryGetValue(out double reported) ? reported : 0;
            long resetsAt = Number(window["resetsAt"]);
            limits.Add(new PlanLimit(label, Math.Round(utilization * 100), resetsAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(resetsAt) : null));
        }

        return limits;
    }

    /// <summary>The same limits from the usage report of /usage, which names them by kind.</summary>
    internal static IReadOnlyList<PlanLimit> UsageLimits(JsonNode? report)
    {
        List<PlanLimit> limits = [];
        foreach (JsonNode limit in (report?["rate_limits"]?["limits"] as JsonArray ?? []).OfType<JsonNode>())
        {
            string? label = limit["kind"]?.GetValue<string>() switch
            {
                "session" => "5-hour limit",
                "weekly_all" => "Weekly limit",
                _ => null,
            };
            if (label is null)
            {
                continue;
            }

            double percent = limit["percent"] is JsonValue value && value.TryGetValue(out double reported) ? reported : 0;
            DateTimeOffset? resetsAt = DateTimeOffset.TryParse(limit["resets_at"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
                ? parsed.ToUniversalTime()
                : null;
            limits.Add(new PlanLimit(label, Math.Round(percent), resetsAt));
        }

        return limits;
    }

    internal static string PermissionMode(string mode) => mode switch
    {
        "plan" => "plan",
        "autopilot" => "acceptEdits",
        _ => "default",
    };

    private async Task<string> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        using Process process = Process.Start(new ProcessStartInfo(options.CliPath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Claude Code could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        _ = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return await output.ConfigureAwait(false);
    }

    // Without a solution Claude Code still needs a working directory; reads there run without asking, so it is an empty one.
    private static string NeutralDirectory() =>
        Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeTwin", "chat")).FullName;

    private static string? SolutionFolder(string? solution) =>
        string.IsNullOrEmpty(solution) ? null : Path.GetDirectoryName(solution);

    private static IEnumerable<JsonNode> Blocks(JsonNode message, string type) =>
        message["message"]?["content"] is JsonArray blocks
            ? blocks.OfType<JsonNode>().Where(block => block["type"]?.GetValue<string>() == type)
            : [];

    private static string? Text(JsonNode input, string name) =>
        input[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static long Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out long number) ? number : 0;

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
