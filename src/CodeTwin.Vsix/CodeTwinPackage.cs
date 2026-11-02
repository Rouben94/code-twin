using System;
using System.ComponentModel.Design;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace CodeTwin.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideToolWindow(typeof(ChatToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids.SolutionExplorer)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(ChatOptionsPage), "CodeTwin", "Chat", 0, 0, true)]
public sealed class CodeTwinPackage : AsyncPackage
{
    public const string PackageGuidString = "4a8e2c71-9d3b-4f56-8e0a-1c7b5d9f2e63";
    private const string RepositoryUrl = "https://github.com/Rouben94/code-twin";
    private static readonly Guid CommandSet = new("8f3a1d6c-2b7e-4c95-a0d4-6e1f9b3c7a28");

    private CodeTwinMcpHost? mcpHost;
    private ChatServices? chatServices;

    private static string Version =>
        typeof(CodeTwinPackage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "unknown";

    public override IVsAsyncToolWindowFactory? GetAsyncToolWindowFactory(Guid toolWindowType) =>
        toolWindowType == typeof(ChatToolWindow).GUID ? this : null;

    protected override string GetToolWindowTitle(Type toolWindowType, int id) =>
        toolWindowType == typeof(ChatToolWindow) ? ChatToolWindow.Title : base.GetToolWindowTitle(toolWindowType, id);

    protected override Task<object?> InitializeToolWindowAsync(Type toolWindowType, int id, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(chatServices);

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = await GetServiceAsync(typeof(SDTE)) as DTE
            ?? throw new InvalidOperationException("The DTE service is not available.");
        var errorList = await GetServiceAsync(typeof(SVsErrorList)) as IErrorList
            ?? throw new InvalidOperationException("The Error List service is not available.");
        var reader = new PlcTreeReader(dte);
        mcpHost = CodeTwinMcpHost.Start(
            new UiThreadPlcAutomation(JoinableTaskFactory, reader, new ErrorListReader(JoinableTaskFactory, errorList)));
        chatServices = new ChatServices(
            mcpHost.Endpoint, mcpHost.Token, new ChatContextProvider(dte, reader), JoinableTaskFactory, (ChatOptionsPage)GetDialogPage(typeof(ChatOptionsPage)));

        if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
        {
            AddCommand(commands, 0x0100, ShowChat);
            AddCommand(commands, 0x0101, () => VsShellUtilities.OpenSystemBrowser($"{RepositoryUrl}#readme"));
            AddCommand(commands, 0x0102, () => VsShellUtilities.OpenSystemBrowser($"{RepositoryUrl}/issues/new"));
            AddCommand(commands, 0x0103, ShowAbout);
            AddCommand(commands, 0x0104, () => ShowOptionPage(typeof(ChatOptionsPage)));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            mcpHost?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static void AddCommand(OleMenuCommandService commands, int id, Action execute) =>
        commands.AddCommand(new MenuCommand((_, _) => execute(), new CommandID(CommandSet, id)));

    private void ShowChat() =>
        JoinableTaskFactory.RunAsync(() => ShowToolWindowAsync(typeof(ChatToolWindow), 0, create: true, DisposalToken))
            .FileAndForget("CodeTwin/ShowChat");

    private void ShowAbout() =>
        VsShellUtilities.ShowMessageBox(
            this,
            $"CodeTwin {Version}\n\nMCP server: {mcpHost?.Endpoint}\n\nLicensed under the Mozilla Public License 2.0.\n{RepositoryUrl}",
            "About CodeTwin",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
}
