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

string? cliPath = CopilotCliLocator.Locate();
if (cliPath is null)
{
    channel.Error($"GitHub Copilot CLI (copilot.exe) was not found on PATH. Install it (winget install GitHub.Copilot), run `copilot login`, then reopen this window. You can also point {CopilotCliLocator.PathVariable} at copilot.exe.");
    return 1;
}

ChatOptions options = new(
    cliPath,
    mcpUrl,
    mcpToken,
    Path.Combine(AppContext.BaseDirectory, "agents"),
    Path.Combine(AppContext.BaseDirectory, "skills"));

await using ChatHost host = new(options, channel);
try
{
    await host.StartAsync(CancellationToken.None);
}
catch (Exception exception)
{
    channel.Error($"GitHub Copilot CLI could not be started: {exception.Message}");
    return 1;
}

channel.Ready();

try
{
    await host.PublishModelsAsync(CancellationToken.None);
    await host.PublishQuotaAsync(CancellationToken.None);
}
catch (Exception exception)
{
    await Console.Error.WriteLineAsync($"Reading the Copilot models or quota failed: {exception.Message}");
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
            await host.NewChatAsync();
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
    }
}

static string? GetOptionalString(JsonElement element, string name) =>
    element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
