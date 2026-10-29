using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeTwin.Chat;

/// <summary>
/// One Claude Code process in stream-json mode: user messages and control requests go to stdin as JSON lines,
/// events come back on stdout. This is the protocol the Claude Agent SDKs use (there is no .NET SDK).
/// </summary>
internal sealed class ClaudeCli : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly Process process;
    private readonly Lock gate = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> pendingRequests = new(StringComparer.Ordinal);
    private readonly Func<JsonNode, Task> onMessage;
    private readonly Task reader;
    private int requestCounter;

    private ClaudeCli(Process process, Func<JsonNode, Task> onMessage)
    {
        this.process = process;
        this.onMessage = onMessage;
        reader = Task.Run(ReadAsync);
    }

    public bool HasExited => process.HasExited;

    public static IEnumerable<string> StreamArguments =>
        ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--include-partial-messages", "--permission-prompt-tool", "stdio"];

    public static ClaudeCli Start(string cliPath, string workingDirectory, IEnumerable<string> arguments, Func<JsonNode, Task> onMessage)
    {
        ProcessStartInfo startInfo = new(cliPath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Claude Code could not be started.");
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Console.Error.WriteLine($"claude: {e.Data}");
            }
        };
        process.BeginErrorReadLine();
        return new ClaudeCli(process, onMessage);
    }

    /// <summary>Sends a user message whose content is plain text or a list of content blocks.</summary>
    public void SendUserMessage(object content) =>
        Write(new { type = "user", message = new { role = "user", content }, parent_tool_use_id = (string?)null, session_id = string.Empty });

    /// <summary>Sends a control request (initialize, interrupt, set_model, ...) and returns its response.</summary>
    public async Task<JsonNode?> RequestAsync(object request, CancellationToken cancellationToken)
    {
        string id = "codetwin-" + Interlocked.Increment(ref requestCounter).ToString(CultureInfo.InvariantCulture);
        TaskCompletionSource<JsonNode?> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRequests[id] = response;
        Write(new { type = "control_request", request_id = id, request });
        using (cancellationToken.Register(() => response.TrySetCanceled(cancellationToken)))
        {
            return await response.Task.ConfigureAwait(false);
        }
    }

    /// <summary>Answers a control request from Claude Code, such as can_use_tool.</summary>
    public void Respond(string requestId, object response) =>
        Write(new { type = "control_response", response = new { subtype = "success", request_id = requestId, response } });

    public async ValueTask DisposeAsync()
    {
        try
        {
            lock (gate)
            {
                process.StandardInput.Close();
            }

            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        await reader.ConfigureAwait(false);
        process.Dispose();
    }

    private void Write(object message)
    {
        string line = JsonSerializer.Serialize(message, SerializerOptions);
        lock (gate)
        {
            process.StandardInput.WriteLine(line);
            process.StandardInput.Flush();
        }
    }

    private async Task ReadAsync()
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                JsonNode? message;
                try
                {
                    message = JsonNode.Parse(line);
                }
                catch (JsonException)
                {
                    await Console.Error.WriteLineAsync($"claude: {line}").ConfigureAwait(false);
                    continue;
                }

                if (message is null)
                {
                    continue;
                }

                if (message["type"]?.GetValue<string>() == "control_response")
                {
                    CompleteRequest(message["response"]);
                    continue;
                }

                try
                {
                    await onMessage(message).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await Console.Error.WriteLineAsync($"Handling a Claude Code event failed: {exception.Message}").ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }

        foreach (TaskCompletionSource<JsonNode?> pending in pendingRequests.Values)
        {
            pending.TrySetException(new InvalidOperationException("Claude Code exited."));
        }
    }

    private void CompleteRequest(JsonNode? response)
    {
        string? id = response?["request_id"]?.GetValue<string>();
        if (id is null || !pendingRequests.TryRemove(id, out TaskCompletionSource<JsonNode?>? pending))
        {
            return;
        }

        if (response!["subtype"]?.GetValue<string>() == "error")
        {
            pending.TrySetException(new InvalidOperationException(response["error"]?.ToString() ?? "Claude Code rejected the request."));
        }
        else
        {
            pending.TrySetResult(response["response"]);
        }
    }
}
