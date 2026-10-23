using System.Diagnostics;
using System.Text.Json;

namespace CodeTwin.Mcp;

internal sealed record ShellInstance(int Pid, Uri Endpoint, string Token);

/// <summary>Reads the discovery files that each CodeTwin VSIX instance writes (port and token only).</summary>
internal static class ShellDiscovery
{
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodeTwin",
        "instances");

    public static IReadOnlyList<ShellInstance> GetRunningShells(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var shells = new List<ShellInstance>();
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (TryRead(file) is { } shell && IsRunning(shell.Pid))
            {
                shells.Add(shell);
            }
        }

        return shells;
    }

    public static ShellInstance? Find(string directory, int pid) =>
        GetRunningShells(directory).FirstOrDefault(shell => shell.Pid == pid);

    public static bool IsRunning(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static ShellInstance? TryRead(string file)
    {
        try
        {
            using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            DiscoveryFile? discovery = JsonSerializer.Deserialize<DiscoveryFile>(stream, JsonSerializerOptions.Web);
            return discovery is { Pid: > 0, Endpoint: not null, Token: not null }
                && Uri.TryCreate(discovery.Endpoint, UriKind.Absolute, out Uri? endpoint)
                ? new ShellInstance(discovery.Pid, endpoint, discovery.Token)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // The shell may be writing or deleting the file right now; it is picked up on the next lookup.
            return null;
        }
    }

    private sealed record DiscoveryFile(int Pid, string? Endpoint, string? Token);
}
