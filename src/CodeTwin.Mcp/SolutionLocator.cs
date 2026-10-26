using System.Text.RegularExpressions;

namespace CodeTwin.Mcp;

/// <summary>Finds the TwinCAT solution (.sln that references a .tsproj) for repository, folder or project file paths.</summary>
/// <remarks>Returns null when an existing path has no TwinCAT solution; throws for invalid paths and ambiguous results.</remarks>
internal static partial class SolutionLocator
{
    private const int MaxSearchDepth = 8;

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "_Boot", "_CompileInfo", "_Libraries", "_Deployment",
    };

    /// <summary>Finds the one TwinCAT solution across several paths, e.g. the repositories of a multi-repo session.</summary>
    public static string? Locate(IReadOnlyCollection<string> paths)
    {
        List<string> solutions = paths
            .Select(Locate)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Single(solutions, string.Join("', '", paths));
    }

    public static string? Locate(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"Pass an absolute path, not '{path}'.");
        }

        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            if (string.Equals(Path.GetExtension(fullPath), ".sln", StringComparison.OrdinalIgnoreCase))
            {
                return GetTwinCatProjects(fullPath).Count > 0
                    ? fullPath
                    : throw new ArgumentException($"'{fullPath}' does not contain a TwinCAT project (.tsproj).");
            }

            return Single(FindAbove(Path.GetDirectoryName(fullPath)!, fullPath), fullPath);
        }

        if (Directory.Exists(fullPath))
        {
            List<string> below = [];
            FindBelow(fullPath, 0, below);
            return Single(below.Count > 0 ? below : FindAbove(fullPath, fullPath), fullPath);
        }

        throw new ArgumentException($"'{fullPath}' does not exist.");
    }

    private static string? Single(IReadOnlyList<string> solutions, string path) => solutions.Count switch
    {
        1 => solutions[0],
        0 => null,
        _ => throw new ArgumentException(
            $"Several TwinCAT solutions found for '{path}': {string.Join(", ", solutions)}. Call initialize again with the .sln path."),
    };

    private static void FindBelow(string directory, int depth, List<string> solutions)
    {
        IEnumerable<string> files;
        IEnumerable<string> subdirectories;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.sln").ToList();
            subdirectories = Directory.EnumerateDirectories(directory).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        solutions.AddRange(files.Where(file => GetTwinCatProjects(file).Count > 0));
        if (depth == MaxSearchDepth)
        {
            return;
        }

        foreach (string subdirectory in subdirectories)
        {
            string name = Path.GetFileName(subdirectory);
            if (!name.StartsWith('.') && !SkippedDirectories.Contains(name))
            {
                FindBelow(subdirectory, depth + 1, solutions);
            }
        }
    }

    private static List<string> FindAbove(string startDirectory, string target)
    {
        for (DirectoryInfo? directory = new(startDirectory); directory is not null; directory = directory.Parent)
        {
            List<string> solutions = directory.EnumerateFiles("*.sln")
                .Select(file => file.FullName)
                .Where(solution => GetTwinCatProjects(solution).Any(project => Contains(project, target)))
                .ToList();
            if (solutions.Count > 0)
            {
                return solutions;
            }
        }

        return [];
    }

    private static bool Contains(string tsproj, string target)
    {
        string projectDirectory = Path.GetDirectoryName(tsproj)! + Path.DirectorySeparatorChar;
        return string.Equals(tsproj, target, StringComparison.OrdinalIgnoreCase)
            || (target + Path.DirectorySeparatorChar).StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> GetTwinCatProjects(string solution)
    {
        string directory = Path.GetDirectoryName(solution)!;
        return File.ReadLines(solution)
            .Select(line => ProjectLine().Match(line))
            .Where(match => match.Success)
            .Select(match => Path.GetFullPath(Path.Combine(directory, match.Groups["path"].Value)))
            .ToList();
    }

    [GeneratedRegex("""^Project\("\{[^}]+\}"\)\s*=\s*"[^"]*"\s*,\s*"(?<path>[^"]+\.tsproj)"\s*,""", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectLine();
}
