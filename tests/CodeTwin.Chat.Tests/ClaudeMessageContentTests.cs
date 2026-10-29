using System.Text.Json;
using CodeTwin.Chat;
using Xunit;

namespace CodeTwin.Chat.Tests;

public sealed class ClaudeMessageContentTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("codetwin-attachments-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public async Task SendsPlainTextWithoutInlineAttachments()
    {
        string notes = WriteFile("notes.txt", 10);

        object content = await ClaudeMessageContent.CreateAsync("Read my notes", [notes], CancellationToken.None);

        Assert.Equal("Read my notes", content);
    }

    [Fact]
    public async Task InlinesImagesAndPdfsAsBase64Blocks()
    {
        string image = WriteFile("Screen.PNG", 3);
        string document = WriteFile("spec.pdf", 3);

        object content = await ClaudeMessageContent.CreateAsync("Compare", [image, document], CancellationToken.None);

        Assert.Equal(
            """[{"type":"text","text":"Compare"},{"type":"image","source":{"type":"base64","media_type":"image/png","data":"AAAA"}},{"type":"document","source":{"type":"base64","media_type":"application/pdf","data":"AAAA"}}]""",
            JsonSerializer.Serialize(content));
    }

    [Fact]
    public async Task LeavesImagesOverTheLimitToTheReadTool()
    {
        string image = WriteFile("huge.jpg", ClaudeMessageContent.MaxImageBytes + 1);

        object content = await ClaudeMessageContent.CreateAsync("Look", [image], CancellationToken.None);

        Assert.Equal("Look", content);
    }

    private string WriteFile(string name, long length)
    {
        string path = Path.Combine(folder, name);
        using FileStream stream = File.Create(path);
        stream.SetLength(length);
        return path;
    }
}
