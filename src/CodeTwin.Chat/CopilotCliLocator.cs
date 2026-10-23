namespace CodeTwin.Chat;

internal static class CopilotCliLocator
{
    public const string PathVariable = "COPILOT_CLI_PATH";
    private const string ExecutableName = "copilot.exe";

    public static string? Locate() =>
        Locate(Environment.GetEnvironmentVariable(PathVariable), Environment.GetEnvironmentVariable("PATH"), File.Exists);

    public static string? Locate(string? configuredPath, string? searchPath, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return fileExists(configuredPath) ? configuredPath : null;
        }

        foreach (string directory in (searchPath ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(directory.Trim('"'), ExecutableName);
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
