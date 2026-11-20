using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CodeTwin.Vsix;

/// <summary>Tools > Options > CodeTwin > Updates. Stored per user.</summary>
[ComVisible(true)]
[Guid("d3a9f1c6-5e2b-4a87-b0c4-7f6e1d2a9b58")]
public sealed class UpdateOptionsPage : DialogPage
{
    [Category("Updates")]
    [DisplayName("Check for updates")]
    [Description("Checks GitHub for a new CodeTwin release when a solution is first opened and offers to install it.")]
    public bool CheckForUpdates { get; set; } = true;

    [Browsable(false)]
    public string SkippedVersion { get; set; } = string.Empty;
}
