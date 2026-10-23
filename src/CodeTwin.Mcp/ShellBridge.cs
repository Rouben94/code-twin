using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CodeTwin.Mcp;

internal sealed record InitializeResult(string Solution, int Pid, bool Started);

/// <summary>Binds the session to one TwinCAT XAE Shell and forwards MCP tool calls to its HTTP endpoint.</summary>
/// <remarks>
/// The bound solution is kept in memory only, so a restarted shell is found again without being re-initialized.
/// Tool calls need <see cref="InitializeAsync"/> first, so a session never edits a project it was not pointed at.
/// </remarks>
internal sealed class ShellBridge(
    HttpClient http,
    string instancesDirectory,
    Func<int, CancellationToken, Task<string?>>? openSolution = null) : IDisposable
{
    private readonly Func<int, CancellationToken, Task<string?>> getOpenSolution = openSolution ?? RunningSolutions.GetOpenSolutionAsync;
    private readonly SemaphoreSlim gate = new(1, 1);
    private ShellInstance? shell;
    private string? solution;
    private int nextRequestId;

    public async Task<InitializeResult> InitializeAsync(string path, CancellationToken cancellationToken)
    {
        string? located = SolutionLocator.Locate(path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InitializeResult result = located is null
                ? await BindOnlyOpenSolutionAsync(path, cancellationToken).ConfigureAwait(false)
                : await BindAsync(located, cancellationToken).ConfigureAwait(false);
            solution = result.Solution;
            return result;
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

    private async Task<InitializeResult> BindAsync(string located, CancellationToken cancellationToken)
    {
        ShellInstance? found = await FindShellWithSolutionAsync(located, cancellationToken).ConfigureAwait(false);
        bool started = found is null;
        found ??= await ShellLauncher.StartAsync(
            located,
            token => FindShellWithSolutionAsync(located, token),
            cancellationToken).ConfigureAwait(false);
        shell = found;
        return new InitializeResult(located, found.Pid, started);
    }

    private async Task<InitializeResult> BindOnlyOpenSolutionAsync(string path, CancellationToken cancellationToken)
    {
        var open = new List<(ShellInstance Shell, string Solution)>();
        foreach (ShellInstance candidate in ShellDiscovery.GetRunningShells(instancesDirectory))
        {
            if (await getOpenSolution(candidate.Pid, cancellationToken).ConfigureAwait(false) is { } openSolutionPath)
            {
                open.Add((candidate, openSolutionPath));
            }
        }

        switch (open.Count)
        {
            case 1:
                shell = open[0].Shell;
                return new InitializeResult(open[0].Solution, open[0].Shell.Pid, Started: false);
            case 0:
                throw new ArgumentException(
                    $"No TwinCAT solution (.sln with a .tsproj) found for '{path}', and no TwinCAT XAE Shell with CodeTwin has a solution open. "
                    + "Ask the user for the path of the TwinCAT repository, .sln or .plcproj.");
            default:
                throw new ArgumentException(
                    $"No TwinCAT solution found for '{path}', and several TwinCAT XAE Shells are open: "
                    + string.Join("; ", open.Select(item => $"pid {item.Shell.Pid}: {item.Solution}"))
                    + ". Call initialize again with the .sln path of the one to use (ask the user if unclear).");
        }
    }

    private async Task<ShellInstance> GetShellAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (shell is not null && ShellDiscovery.IsRunning(shell.Pid))
            {
                return shell;
            }

            if (solution is null)
            {
                throw new InvalidOperationException(
                    "Not connected to a TwinCAT project. Call initialize first with the absolute path of the repository or .plcproj "
                    + "you work on (the current working directory if the user named none).");
            }

            shell = await FindShellWithSolutionAsync(solution, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"No running TwinCAT XAE Shell has '{solution}' open. Call initialize again to start one.");
            return shell;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ShellInstance?> FindShellWithSolutionAsync(string path, CancellationToken cancellationToken)
    {
        foreach (ShellInstance candidate in ShellDiscovery.GetRunningShells(instancesDirectory))
        {
            if (ShellLauncher.PathEquals(await getOpenSolution(candidate.Pid, cancellationToken).ConfigureAwait(false), path))
            {
                return candidate;
            }
        }

        return null;
    }
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
