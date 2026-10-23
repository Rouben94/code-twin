using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CodeTwin.Mcp;

/// <summary>Starts TcXaeShell with a solution and waits until the CodeTwin extension in it is reachable.</summary>
internal static class ShellLauncher
{
    private const string AppEnvKey = @"SOFTWARE\Microsoft\AppEnv\17.0\Apps\TcXaeShell_17.0";
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public static async Task<ShellInstance> StartAsync(
        string solution,
        Func<CancellationToken, Task<ShellInstance?>> findStarted,
        CancellationToken cancellationToken)
    {
        Launch(solution);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartTimeout);
        try
        {
            while (true)
            {
                await Task.Delay(PollInterval, timeout.Token).ConfigureAwait(false);
                if (await findStarted(timeout.Token).ConfigureAwait(false) is { } shell)
                {
                    return shell;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"TcXaeShell did not open '{solution}' with the CodeTwin extension within {StartTimeout.TotalMinutes} minutes. "
                + "Check the shell for open dialogs and that the CodeTwin extension is installed, then call initialize again.");
        }
    }

    public static bool PathEquals(string? left, string right) =>
        left is not null && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void Launch(string solution)
    {
        string executable = FindExecutable();
        string arguments = $"\"{solution}\"";
        try
        {
            // Started by Explorer, the shell survives the MCP client, which kills its job (and the bridge) on exit.
            DesktopShell.Execute(executable, arguments, Path.GetDirectoryName(solution)!);
            Console.Error.WriteLine($"code-twin-mcp: started TcXaeShell through Explorer with {solution}");
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or InvalidOperationException)
        {
            Console.Error.WriteLine($"code-twin-mcp: Explorer launch failed ({exception.Message}); starting TcXaeShell directly, it closes with the MCP client.");
            using Process process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = true })
                ?? throw new InvalidOperationException("TcXaeShell could not be started.");
        }
    }

    private static string FindExecutable()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(AppEnvKey);
        return key?.GetValue("StubExePath") is string path && File.Exists(path)
            ? path
            : throw new InvalidOperationException("TwinCAT XAE Shell (TcXaeShell 17.0) is not installed.");
    }
}
