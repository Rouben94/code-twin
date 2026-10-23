using System.Text;

namespace CodeTwin.Chat;

internal static class ChatContext
{
    public static string CreateSystemMessage(string? solution) =>
        $"""
        You run inside the CodeTwin chat window of the TwinCAT XAE Shell. The `codetwin` tools are already connected to this shell{(string.IsNullOrEmpty(solution) ? ", but no solution is open yet" : $", which has the solution `{solution}` open")}.
        There is no `initialize` tool here and you never need to connect: skip the Connect step of your workflow and start with the PLC tools.
        Each user message starts with a <context> block that names the open solution and the PLC element in the active editor. Treat "this POU", "here" or "the open file" as that element.
        """;

    public static string CreatePrompt(string prompt, string? solution, string? activeElement)
    {
        StringBuilder builder = new();
        builder.AppendLine("<context>");
        builder.AppendLine($"Solution: {(string.IsNullOrEmpty(solution) ? "(none open)" : solution)}");
        builder.AppendLine($"Active element: {(string.IsNullOrEmpty(activeElement) ? "(none)" : activeElement)}");
        builder.AppendLine("</context>");
        builder.AppendLine();
        builder.Append(prompt);
        return builder.ToString();
    }
}
