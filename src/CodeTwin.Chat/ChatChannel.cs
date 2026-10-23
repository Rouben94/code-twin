using System.Text;
using System.Text.Json;

namespace CodeTwin.Chat;

internal sealed class ChatChannel(TextWriter output)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly Lock gate = new();

    public static ChatChannel ForStandardOutput() =>
        new(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true });

    public void Send(object message)
    {
        string line = JsonSerializer.Serialize(message, SerializerOptions);
        lock (gate)
        {
            output.WriteLine(line);
        }
    }

    public void Ready() => Send(new { type = "ready" });

    public void Error(string text) => Send(new { type = "error", text });
}
