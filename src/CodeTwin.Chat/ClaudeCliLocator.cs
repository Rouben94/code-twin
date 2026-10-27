namespace CodeTwin.Chat;

// Only a Claude Code the user installed: the copy inside the Claude desktop app sits in its MSIX package folder.
internal static class ClaudeCliLocator
{
    public const string PathVariable = "CLAUDE_CLI_PATH";
    private const string ExecutableName = "claude.exe";

    public static string? Locate() =>
        Locate(
            Environment.GetEnvironmentVariable(PathVariable),
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            File.Exists);

    public static string? Locate(string? configuredPath, string? searchPath, string? userProfile, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return fileExists(configuredPath) ? configuredPath : null;
        }

        IEnumerable<string> directories = (searchPath ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => directory.Trim('"'));

        // The native installer puts claude.exe here and asks the user to add it to PATH, which many skip.
        if (!string.IsNullOrEmpty(userProfile))
        {
            directories = directories.Append(Path.Combine(userProfile, ".local", "bin"));
        }

        return directories
            .Select(directory => Path.Combine(directory, ExecutableName))
            .FirstOrDefault(fileExists);
    }
}
