namespace CodeTwin.Chat;

/// <summary>
/// Builds the content of a Claude Code user message. Attached images and PDFs go into the message as base64 blocks;
/// every other attached file, and any too large to send inline, Claude opens by the path named in the context block.
/// </summary>
internal static class ClaudeMessageContent
{
    // Claude rejects base64 images over 5 MB and requests over 32 MB.
    internal const long MaxImageBytes = 3_750_000;
    internal const long MaxDocumentBytes = 20_000_000;

    public static async Task<object> CreateAsync(string text, IReadOnlyList<string> attachments, CancellationToken cancellationToken)
    {
        List<object> blocks = [new { type = "text", text }];
        foreach (string path in attachments)
        {
            if (InlineKind(path) is not (string type, string mediaType, long limit) || new FileInfo(path).Length > limit)
            {
                continue;
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            blocks.Add(new { type, source = new { type = "base64", media_type = mediaType, data = Convert.ToBase64String(bytes) } });
        }

        return blocks.Count == 1 ? text : blocks;
    }

    private static (string Type, string MediaType, long Limit)? InlineKind(string path) =>
        Path.GetExtension(path).ToUpperInvariant() switch
        {
            ".PNG" => ("image", "image/png", MaxImageBytes),
            ".JPG" or ".JPEG" => ("image", "image/jpeg", MaxImageBytes),
            ".GIF" => ("image", "image/gif", MaxImageBytes),
            ".WEBP" => ("image", "image/webp", MaxImageBytes),
            ".PDF" => ("document", "application/pdf", MaxDocumentBytes),
            _ => null,
        };
}
