using System.Text.Json.Nodes;
using CodeTwin.Chat;
using Xunit;

namespace CodeTwin.Chat.Tests;

public sealed class ClaudeTranscriptsTests : IDisposable
{
    private const string Agent = "codetwin:twincat";
    private readonly string folder = Directory.CreateTempSubdirectory("codetwin-transcripts-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void NamesTheProjectFolderLikeClaudeCode()
    {
        Assert.Equal(
            @"C:\p\C--Repos-Machine-1-PLC",
            ClaudeTranscripts.ProjectFolder(@"C:\p", @"C:\Repos\Machine_1.PLC\"));
    }

    [Fact]
    public void ListsOnlyChatsOfTheAgentNewestFirst()
    {
        Write("a", DateTime.UtcNow.AddHours(-1), AgentSetting(Agent), User("<context>\nSolution: x\n</context>\n\nFix MAIN"));
        Write("b", DateTime.UtcNow, AgentSetting(Agent), User("Build it"));
        Write("other", DateTime.UtcNow, AgentSetting("someone:else"), User("Hello"));

        IReadOnlyList<ClaudeTranscript> transcripts = ClaudeTranscripts.List(folder, Agent);

        Assert.Equal(["b", "a"], transcripts.Select(transcript => transcript.Id));
        Assert.Equal("Fix MAIN", transcripts[1].Summary);
    }

    [Fact]
    public void ReplaysTextAndToolsOfTheMainThreadOnly()
    {
        Write(
            "chat",
            DateTime.UtcNow,
            AgentSetting(Agent),
            User("Build"),
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Building."},{"type":"tool_use","id":"t1","name":"mcp__codetwin__plc_build"}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","is_error":true}]}}""",
            """{"type":"assistant","isSidechain":true,"message":{"content":[{"type":"text","text":"subagent"}]}}""",
            """{"type":"user","isMeta":true,"message":{"content":"reminder"}}""",
            "not json");

        string history = string.Join("\n", ClaudeTranscripts.History(folder, "chat").Select(item => System.Text.Json.JsonSerializer.Serialize(item)));

        Assert.Equal(
            """
            {"kind":"user","text":"Build"}
            {"kind":"assistant","text":"Building."}
            {"kind":"tool","id":"t1","name":"plc_build","status":"running"}
            {"kind":"tool","id":"t1","status":"failed"}
            """.ReplaceLineEndings("\n"),
            history);
    }

    [Fact]
    public void DeletesTheChatAndItsDetails()
    {
        Write("chat", DateTime.UtcNow, AgentSetting(Agent));
        Directory.CreateDirectory(Path.Combine(folder, "chat", "subagents"));

        ClaudeTranscripts.Delete(folder, "chat");

        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }

    [Theory]
    [InlineData("mcp__codetwin__plc_build", "plc_build")]
    [InlineData("mcp__plugin_twincat_codetwin__initialize", "initialize")]
    [InlineData("Bash", "Bash")]
    public void NamesMcpToolsByTheToolAlone(string name, string expected)
    {
        Assert.Equal(expected, ClaudeTranscripts.ToolName(name));
    }

    [Theory]
    [InlineData("Bash", """{"command":"dir"}""", "shell", "dir")]
    [InlineData("Edit", """{"file_path":"C:\\x.txt"}""", "write", @"C:\x.txt")]
    [InlineData("WebFetch", """{"url":"https://example.com"}""", "url", "https://example.com")]
    [InlineData("mcp__other__tool", "{}", "mcp", "other/tool")]
    public void DescribesPermissionsWithTheChatsKinds(string tool, string input, string kind, string detail)
    {
        Assert.Equal((kind, detail), ClaudeChatHost.Describe(tool, JsonNode.Parse(input)!));
    }

    [Theory]
    [InlineData("interactive", "default")]
    [InlineData("plan", "plan")]
    [InlineData("autopilot", "acceptEdits")]
    public void MapsTheChatModes(string mode, string permissionMode)
    {
        Assert.Equal(permissionMode, ClaudeChatHost.PermissionMode(mode));
    }

    [Theory]
    [InlineData("opus", "claude-opus-5-5", "opus")]
    [InlineData(null, "claude-opus-5-5", "default")]
    [InlineData(null, "claude-sonnet-5-5", "sonnet")]
    [InlineData(null, "claude-unknown-1", "claude-unknown-1")]
    public void ReportsTheModelAsThePickerListsIt(string? selected, string running, string expected)
    {
        (string, string?)[] models = [("default", "claude-opus-5-5"), ("opus", "claude-opus-5-5"), ("sonnet", "claude-sonnet-5-5")];

        Assert.Equal(expected, ClaudeChatHost.PickerModel(selected, running, models));
    }

    [Fact]
    public void ReadsTheFiveHourAndWeeklyLimits()
    {
        JsonNode message = JsonNode.Parse("""
            {"type":"rate_limit_event","rate_limit_info":{"status":"allowed","unifiedWindows":{
              "five_hour":{"utilization":0.09,"resetsAt":1793119800},
              "seven_day":{"utilization":0.034,"resetsAt":1793390400}}}}
            """)!;

        Assert.Equal(
            [
                new PlanLimit("5-hour limit", 9, DateTimeOffset.FromUnixTimeSeconds(1793119800)),
                new PlanLimit("Weekly limit", 3, DateTimeOffset.FromUnixTimeSeconds(1793390400)),
            ],
            ClaudeChatHost.PlanLimits(message));
        Assert.Empty(ClaudeChatHost.PlanLimits(JsonNode.Parse("""{"type":"rate_limit_event","rate_limit_info":{}}""")!));
    }

    [Fact]
    public void ReadsTheLimitsFromTheUsageReport()
    {
        JsonNode report = JsonNode.Parse("""
            {"rate_limits":{"limits":[
              {"kind":"session","group":"session","percent":40,"resets_at":"2026-10-27T17:49:59.5+01:00"},
              {"kind":"weekly_all","group":"weekly","percent":7,"resets_at":null},
              {"kind":"weekly_model","group":"weekly","percent":3,"resets_at":null}]}}
            """)!;

        Assert.Equal(
            [
                new PlanLimit("5-hour limit", 40, new DateTimeOffset(2026, 10, 27, 16, 49, 59, 500, TimeSpan.Zero)),
                new PlanLimit("Weekly limit", 7, null),
            ],
            ClaudeChatHost.UsageLimits(report));
        Assert.Empty(ClaudeChatHost.UsageLimits(null));
    }

    private static string AgentSetting(string agent) => $$"""{"type":"agent-setting","agentSetting":"{{agent}}"}""";

    private static string User(string text) =>
        new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user", ["content"] = text } }.ToJsonString();

    private void Write(string id, DateTime modified, params string[] lines)
    {
        string file = Path.Combine(folder, id + ".jsonl");
        File.WriteAllLines(file, lines);
        File.SetLastWriteTimeUtc(file, modified);
    }
}
