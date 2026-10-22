using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CodeTwin.Vsix;

[Guid("b1d7a3c4-58e2-4f0a-9c61-7e2d5a8f3b90")]
public sealed class PlcTreeToolWindow : ToolWindowPane
{
    public const string Title = "CodeTwin";

    public PlcTreeToolWindow(object reader)
        : base(null)
    {
        Caption = Title;
        Control = new PlcTreeControl((PlcTreeReader)reader);
        Content = Control;
    }

    internal PlcTreeControl Control { get; }
}
