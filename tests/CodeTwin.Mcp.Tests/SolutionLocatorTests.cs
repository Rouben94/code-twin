using CodeTwin.Mcp;
using Xunit;

namespace CodeTwin.Mcp.Tests;

public sealed class SolutionLocatorTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("codetwin-locator-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void FindsTheSolutionBelowARepositoryFolder()
    {
        string solution = CreateTwinCatSolution(Path.Combine(root, "repo", "Machine"));

        Assert.Equal(solution, SolutionLocator.Locate(Path.Combine(root, "repo")));
    }

    [Fact]
    public void FindsTheSolutionAboveAPlcProject()
    {
        string solution = CreateTwinCatSolution(Path.Combine(root, "repo"));
        string plcProject = Path.Combine(root, "repo", "Machine", "Plc", "Plc.plcproj");

        Assert.Equal(solution, SolutionLocator.Locate(plcProject));
    }

    [Fact]
    public void AcceptsASolutionFile()
    {
        string solution = CreateTwinCatSolution(Path.Combine(root, "repo"));

        Assert.Equal(solution, SolutionLocator.Locate(solution));
    }

    [Fact]
    public void RejectsAmbiguousFolders()
    {
        CreateTwinCatSolution(Path.Combine(root, "repo", "A"));
        CreateTwinCatSolution(Path.Combine(root, "repo", "B"));

        Assert.Throws<ArgumentException>(() => SolutionLocator.Locate(Path.Combine(root, "repo")));
    }

    [Fact]
    public void ReturnsNullForFoldersWithoutTwinCatSolution()
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "empty")).FullName;
        File.WriteAllText(Path.Combine(folder, "Other.sln"), "Project(\"{X}\") = \"Lib\", \"Lib\\Lib.csproj\", \"{Y}\"\r\n");

        Assert.Null(SolutionLocator.Locate(folder));
    }

    [Fact]
    public void RejectsRelativePaths()
    {
        Assert.Throws<ArgumentException>(() => SolutionLocator.Locate("repo"));
    }

    [Fact]
    public void FindsTheOneSolutionAcrossTheRepositoriesOfASession()
    {
        string hmi = Directory.CreateDirectory(Path.Combine(root, "Hmi")).FullName;
        string solution = CreateTwinCatSolution(Path.Combine(root, "Plc"));

        Assert.Equal(solution, SolutionLocator.Locate([hmi, Path.Combine(root, "Plc"), root]));
    }

    [Fact]
    public void RejectsSeveralSolutionsAcrossRepositories()
    {
        CreateTwinCatSolution(Path.Combine(root, "Plc"));
        CreateTwinCatSolution(Path.Combine(root, "PlcLibrary"));

        Assert.Throws<ArgumentException>(
            () => SolutionLocator.Locate([Path.Combine(root, "Plc"), Path.Combine(root, "PlcLibrary")]));
    }

    [Fact]
    public void ReturnsNullWhenNoRepositoryHasATwinCatSolution()
    {
        string hmi = Directory.CreateDirectory(Path.Combine(root, "Hmi")).FullName;
        string docs = Directory.CreateDirectory(Path.Combine(root, "Docs")).FullName;

        Assert.Null(SolutionLocator.Locate([hmi, docs]));
    }

    private static string CreateTwinCatSolution(string folder)
    {
        string plcFolder = Directory.CreateDirectory(Path.Combine(folder, "Machine", "Plc")).FullName;
        File.WriteAllText(Path.Combine(folder, "Machine", "Machine.tsproj"), "<TcSmProject />");
        File.WriteAllText(Path.Combine(plcFolder, "Plc.plcproj"), "<Project />");
        string solution = Path.Combine(folder, "Machine.sln");
        File.WriteAllText(
            solution,
            "Project(\"{B1E792BE-AA5F-4E3C-8C82-674BF9C0715B}\") = \"Machine\", \"Machine\\Machine.tsproj\", \"{1}\"\r\nEndProject\r\n");
        return solution;
    }
}
