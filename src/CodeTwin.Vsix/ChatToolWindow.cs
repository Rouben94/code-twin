using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace CodeTwin.Vsix;

internal sealed class ChatServices(string mcpEndpoint, string mcpToken, ChatContextProvider context, JoinableTaskFactory joinableTaskFactory, ChatOptionsPage options)
{
    public ChatOptionsPage Options => options;

    public string McpEndpoint => mcpEndpoint;

    public string McpToken => mcpToken;

    public ChatContextProvider Context => context;

    public JoinableTaskFactory JoinableTaskFactory => joinableTaskFactory;
}

[Guid("5c2f8e17-6a4d-4b9e-a3f0-8d1c7e2b6a54")]
public sealed class ChatToolWindow : ToolWindowPane
{
    public const string Title = "CodeTwin Chat";

    public ChatToolWindow(object services)
        : base(null)
    {
        Caption = Title;
        Control = new ChatControl((ChatServices)services);
        Content = Control;
    }

    internal ChatControl Control { get; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Control.Dispose();
        }

        base.Dispose(disposing);
    }
}
