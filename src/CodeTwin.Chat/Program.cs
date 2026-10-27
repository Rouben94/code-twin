using System.Text;
using System.Text.Json;
using CodeTwin.Chat;

ChatChannel channel = ChatChannel.ForStandardOutput();

string? mcpUrl = Environment.GetEnvironmentVariable("CODETWIN_MCP_URL");
string? mcpToken = Environment.GetEnvironmentVariable("CODETWIN_MCP_TOKEN");
if (string.IsNullOrEmpty(mcpUrl) || string.IsNullOrEmpty(mcpToken))
{
    channel.Error("The CodeTwin MCP endpoint is not configured. The chat sidecar must be started by the CodeTwin extension.");
    return 1;
}

bool useClaude = string.Equals(Environment.GetEnvironmentVariable("CODETWIN_CHAT_PROVIDER"), "claude", StringComparison.OrdinalIgnoreCase);
string provider = useClaude ? "Claude Code" : "GitHub Copilot CLI";
string? cliPath = useClaude ? ClaudeCliLocator.Locate() : CopilotCliLocator.Locate();
if (cliPath is null)
{
    channel.Error(useClaude
        ? $"Claude Code (claude.exe) was not found on PATH or in %USERPROFILE%\\.local\\bin. Install it (https://code.claude.com/docs/en/setup), run `claude auth login`, then reopen this window. You can also point {ClaudeCliLocator.PathVariable} at claude.exe."
        : $"GitHub Copilot CLI (copilot.exe) was not found on PATH. Install it (winget install GitHub.Copilot), run `copilot login`, then reopen this window. You can also point {CopilotCliLocator.PathVariable} at copilot.exe.");
    return 1;
}

ChatOptions options = new(
    cliPath,
    mcpUrl,
    mcpToken,
    Path.Combine(AppContext.BaseDirectory, "agents"),
    Path.Combine(AppContext.BaseDirectory, "skills"));

await using IChatHost host = useClaude ? new ClaudeChatHost(options, channel) : new ChatHost(options, channel);
try
{
    await host.StartAsync(CancellationToken.None);
}
catch (Exception exception)
{
    channel.Error($"{provider} could not be started: {exception.Message}");
    return 1;
}

channel.Ready(useClaude ? "Claude" : "GitHub Copilot");

try
{
    if (await host.PublishAuthStatusAsync(CancellationToken.None))
    {
        await PublishAccountDataAsync();
    }
}
catch (Exception exception)
{
    await Console.Error.WriteLineAsync($"Reading the {provider} sign-in status failed: {exception.Message}");
}

using StreamReader input = new(Console.OpenStandardInput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
while (await input.ReadLineAsync() is { } line)
{
    try
    {
        await HandleAsync(line);
    }
    catch (JsonException exception)
    {
        channel.Error($"Invalid chat command: {exception.Message}");
    }
    catch (Exception exception)
    {
        channel.Error(exception.Message);
        channel.Send(new { type = "idle" });
    }
}

return 0;

async Task HandleAsync(string line)
{
    using JsonDocument document = JsonDocument.Parse(line);
    JsonElement command = document.RootElement;
    switch (command.GetProperty("type").GetString())
    {
        case "send":
            await host.SendAsync(
                command.GetProperty("prompt").GetString() ?? string.Empty,
                GetOptionalString(command, "solution"),
                GetOptionalString(command, "activeElement"),
                CancellationToken.None);
            break;
        case "abort":
            await host.AbortAsync(CancellationToken.None);
            break;
        case "newChat":
            await host.NewChatAsync(command.TryGetProperty("private", out JsonElement isPrivate) && isPrivate.ValueKind == JsonValueKind.True);
            break;
        case "listSessions":
            await host.PublishSessionsAsync(GetOptionalString(command, "solution"), CancellationToken.None);
            break;
        case "resumeSession":
            await host.ResumeAsync(command.GetProperty("id").GetString() ?? string.Empty, GetOptionalString(command, "solution"), CancellationToken.None);
            break;
        case "deleteSession":
            await host.DeleteAsync(command.GetProperty("id").GetString() ?? string.Empty, CancellationToken.None);
            await host.PublishSessionsAsync(GetOptionalString(command, "solution"), CancellationToken.None);
            break;
        case "permission":
            host.ResolvePermission(command.GetProperty("id").GetString() ?? string.Empty, command.GetProperty("approved").GetBoolean());
            break;
        case "plan":
            host.ResolvePlan(
                command.GetProperty("id").GetString() ?? string.Empty,
                command.GetProperty("approved").GetBoolean(),
                GetOptionalString(command, "action"),
                GetOptionalString(command, "feedback"));
            break;
        case "setModel":
            await host.SetModelAsync(GetOptionalString(command, "model"), GetOptionalString(command, "effort"), CancellationToken.None);
            break;
        case "setMode":
            await host.SetModeAsync(GetOptionalString(command, "mode"), CancellationToken.None);
            break;
        case "setPermissions":
            host.SetToolPermissions(GetOptionalString(command, "permissions"));
            break;
        case "signIn":
            if (await host.SignInAsync(CancellationToken.None))
            {
                await PublishAccountDataAsync();
            }
            break;
    }
}

async Task PublishAccountDataAsync()
{
    try
    {
        await host.PublishModelsAsync(CancellationToken.None);
        await host.PublishQuotaAsync(CancellationToken.None);
    }
    catch (Exception exception)
    {
        await Console.Error.WriteLineAsync($"Reading the {provider} models or usage failed: {exception.Message}");
    }
}

static string? GetOptionalString(JsonElement element, string name) =>
    element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
