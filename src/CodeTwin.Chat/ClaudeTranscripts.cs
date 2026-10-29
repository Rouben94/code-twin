using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodeTwin.Chat;

internal sealed record ClaudeTranscript(string Id, string? Summary, DateTime Modified);

/// <summary>
/// Reads the chats Claude Code stores as JSON lines in its projects folder. The format is Claude Code's own,
/// so only the user and assistant text and the tool calls are read, and anything else is skipped.
/// </summary>
internal static partial class ClaudeTranscripts
{
    private const int SummaryLength = 80;

    // Claude Code names a project's folder after its working directory with every other character replaced by '-'.
    public static string ProjectFolder(string projectsDirectory, string workingDirectory) =>
        Path.Combine(projectsDirectory, NonAlphanumeric().Replace(Path.TrimEndingDirectorySeparator(workingDirectory), "-"));

    /// <summary>The chats in <paramref name="folder"/> that ran with <paramref name="agent"/>, newest first.</summary>
    public static IReadOnlyList<ClaudeTranscript> List(string folder, string agent)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        List<ClaudeTranscript> transcripts = [];
        foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*.jsonl"))
        {
            bool ranWithAgent = false;
            string? summary = null;
            foreach (JsonNode entry in ReadEntries(file.FullName))
            {
                string? type = entry["type"]?.GetValue<string>();
                if (type == "agent-setting" && entry["agentSetting"]?.GetValue<string>() == agent)
                {
                    ranWithAgent = true;
                }
                else if (type == "user" && summary is null && IsMainThread(entry) && UserText(entry) is { Length: > 0 } text)
                {
                    summary = text.Length > SummaryLength ? text[..SummaryLength] + "..." : text;
                }

                if (ranWithAgent && summary is not null)
                {
                    break;
                }
            }

            if (ranWithAgent)
            {
                transcripts.Add(new ClaudeTranscript(Path.GetFileNameWithoutExtension(file.Name), summary, file.LastWriteTimeUtc));
            }
        }

        return [.. transcripts.OrderByDescending(transcript => transcript.Modified)];
    }

    /// <summary>The stored conversation as the chat UI's history items.</summary>
    public static IReadOnlyList<object> History(string folder, string id)
    {
        string file = Path.Combine(folder, id + ".jsonl");
        if (!File.Exists(file))
        {
            return [];
        }

        List<object> items = [];
        foreach (JsonNode entry in ReadEntries(file).Where(IsMainThread))
        {
            switch (entry["type"]?.GetValue<string>())
            {
                case "user":
                    string raw = RawUserText(entry) ?? string.Empty;
                    string text = StripContext(raw).Trim();
                    IReadOnlyList<string> attachments = AttachedFiles(raw);
                    if (text.Length > 0 || attachments.Count > 0)
                    {
                        items.Add(new { kind = "user", text, attachments });
                    }

                    foreach (JsonNode result in Blocks(entry, "tool_result"))
                    {
                        items.Add(new { kind = "tool", id = result["tool_use_id"]?.GetValue<string>(), status = result["is_error"]?.GetValue<bool>() == true ? "failed" : "done" });
                    }

                    break;
                case "assistant":
                    string reply = string.Concat(Blocks(entry, "text").Select(block => block["text"]?.GetValue<string>()));
                    if (reply.Length > 0)
                    {
                        items.Add(new { kind = "assistant", text = reply });
                    }

                    foreach (JsonNode use in Blocks(entry, "tool_use"))
                    {
                        items.Add(new { kind = "tool", id = use["id"]?.GetValue<string>(), name = ToolName(use["name"]?.GetValue<string>()), status = "running" });
                    }

                    break;
            }
        }

        return items;
    }

    public static void Delete(string folder, string id)
    {
        string file = Path.Combine(folder, id + ".jsonl");
        if (File.Exists(file))
        {
            File.Delete(file);
        }

        // Subagent transcripts and tool results of the chat.
        string details = Path.Combine(folder, id);
        if (Directory.Exists(details))
        {
            Directory.Delete(details, recursive: true);
        }
    }

    /// <summary>The prompt without the &lt;context&gt; block that <see cref="ChatContext.CreatePrompt"/> puts in front.</summary>
    public static string StripContext(string text)
    {
        const string end = "</context>";
        int index = text.StartsWith("<context>", StringComparison.Ordinal) ? text.IndexOf(end, StringComparison.Ordinal) : -1;
        return index < 0 ? text : text[(index + end.Length)..].TrimStart('\r', '\n');
    }

    /// <summary>MCP tools are named mcp__&lt;server&gt;__&lt;tool&gt;; the chat UI labels them by the tool name alone.</summary>
    public static string? ToolName(string? name)
    {
        if (name is null || !name.StartsWith("mcp__", StringComparison.Ordinal))
        {
            return name;
        }

        int separator = name.IndexOf("__", "mcp__".Length, StringComparison.Ordinal);
        return separator < 0 ? name : name[(separator + 2)..];
    }

    private static IEnumerable<JsonNode> ReadEntries(string file)
    {
        IEnumerable<string> lines;
        try
        {
            lines = File.ReadLines(file).ToList();
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (string line in lines)
        {
            JsonNode? entry = null;
            try
            {
                entry = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
            }

            if (entry is JsonObject)
            {
                yield return entry;
            }
        }
    }

    private static bool IsMainThread(JsonNode entry) =>
        entry["isSidechain"]?.GetValue<bool>() != true && entry["isMeta"]?.GetValue<bool>() != true;

    /// <summary>The files listed in the &lt;context&gt; block that <see cref="ChatContext.CreatePrompt"/> puts in front.</summary>
    public static IReadOnlyList<string> AttachedFiles(string text)
    {
        int end = text.StartsWith("<context>", StringComparison.Ordinal) ? text.IndexOf("</context>", StringComparison.Ordinal) : -1;
        return end < 0
            ? []
            : [.. text[..end].ReplaceLineEndings("\n").Split('\n')
                .Where(line => line.StartsWith(ChatContext.AttachedFilePrefix, StringComparison.Ordinal))
                .Select(line => line[ChatContext.AttachedFilePrefix.Length..])];
    }

    private static string? UserText(JsonNode entry) =>
        RawUserText(entry) is { Length: > 0 } text ? StripContext(text).Trim() : null;

    private static string? RawUserText(JsonNode entry)
    {
        JsonNode? content = entry["message"]?["content"];
        return content is JsonValue value && value.TryGetValue(out string? plain)
            ? plain
            : content is JsonArray ? string.Concat(Blocks(entry, "text").Select(block => block["text"]?.GetValue<string>())) : null;
    }

    private static IEnumerable<JsonNode> Blocks(JsonNode entry, string type) =>
        entry["message"]?["content"] is JsonArray blocks
            ? blocks.OfType<JsonNode>().Where(block => block["type"]?.GetValue<string>() == type)
            : [];

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
