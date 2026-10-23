using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CodeTwin.Mcp;

internal sealed record InitializeResult(string Solution, int Pid, bool Started);

/// <summary>Binds the session to one TwinCAT XAE Shell and forwards MCP tool calls to its HTTP endpoint.</summary>
/// <remarks>The bound solution is kept in memory only, so a restarted shell is found again without being re-initialized.</remarks>
internal sealed class ShellBridge(HttpClient http, string instancesDirectory) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ShellInstance? shell;
    private string? solution;
    private int nextRequestId;

    public async Task<InitializeResult> InitializeAsync(string path, CancellationToken cancellationToken)
    {
        string located = SolutionLocator.Locate(path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ShellInstance? found = await FindShellWithSolutionAsync(located, cancellationToken).ConfigureAwait(false);
            bool started = found is null;
            found ??= await ShellLauncher.StartAsync(located, instancesDirectory, cancellationToken).ConfigureAwait(false);
            shell = found;
            solution = located;
            return new InitializeResult(located, found.Pid, started);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<JsonElement> CallToolAsync(string name, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        ShellInstance target = await GetShellAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await PostAsync(target, name, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (ShellUnavailableException)
        {
            // The shell restarted (new port and token) or is gone; the request never ran, so retrying is safe.
            Forget(target);
            target = await GetShellAsync(cancellationToken).ConfigureAwait(false);
            return await PostAsync(target, name, arguments, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose() => gate.Dispose();

    private async Task<ShellInstance> GetShellAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (shell is not null && ShellDiscovery.IsRunning(shell.Pid))
            {
                return shell;
            }

            shell = solution is not null
                ? await FindShellWithSolutionAsync(solution, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"No running TwinCAT XAE Shell has '{solution}' open. Call initialize again to start one.")
                : FindOnlyShell();
            return shell;
        }
        finally
        {
            gate.Release();
        }
    }

    private ShellInstance FindOnlyShell()
    {
        IReadOnlyList<ShellInstance> shells = ShellDiscovery.GetRunningShells(instancesDirectory);
        return shells.Count switch
        {
            1 => shells[0],
            0 => throw new InvalidOperationException(
                "No TwinCAT XAE Shell with CodeTwin is running. Call initialize with the repository or .plcproj path."),
            _ => throw new InvalidOperationException(
                "Several TwinCAT XAE Shells are running. Call initialize with the repository or .plcproj path to choose one."),
        };
    }

    private Task<ShellInstance?> FindShellWithSolutionAsync(string path, CancellationToken cancellationToken) =>
        ShellLauncher.FindWithSolutionAsync(instancesDirectory, path, cancellationToken);

    private void Forget(ShellInstance target)
    {
        // Lock-free compare: another call may already have rebound the session.
        Interlocked.CompareExchange(ref shell, null, target);
    }

    private async Task<JsonElement> PostAsync(
        ShellInstance target,
        string name,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken)
    {
        var message = new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref nextRequestId),
            method = "tools/call",
            @params = new { name, arguments = arguments ?? new Dictionary<string, JsonElement>() },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, target.Endpoint)
        {
            Content = JsonContent.Create(message),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.ConnectionError)
        {
            throw new ShellUnavailableException();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new ShellUnavailableException();
            }

            response.EnsureSuccessStatusCode();
            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("error", out JsonElement error))
            {
                throw new InvalidOperationException(error.GetProperty("message").GetString());
            }

            return root.GetProperty("result").Clone();
        }
    }

    private sealed class ShellUnavailableException : Exception;
}
