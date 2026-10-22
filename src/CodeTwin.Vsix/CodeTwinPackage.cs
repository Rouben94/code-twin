using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SolutionEvents = Microsoft.VisualStudio.Shell.Events.SolutionEvents;
using Task = System.Threading.Tasks.Task;

namespace CodeTwin.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideToolWindow(typeof(PlcTreeToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids.SolutionExplorer)]
public sealed class CodeTwinPackage : AsyncPackage
{
    public const string PackageGuidString = "4a8e2c71-9d3b-4f56-8e0a-1c7b5d9f2e63";

    private PlcTreeReader? reader;
    private CodeTwinMcpHost? mcpHost;

    public override IVsAsyncToolWindowFactory? GetAsyncToolWindowFactory(Guid toolWindowType) =>
        toolWindowType == typeof(PlcTreeToolWindow).GUID ? this : null;

    protected override string GetToolWindowTitle(Type toolWindowType, int id) =>
        toolWindowType == typeof(PlcTreeToolWindow) ? PlcTreeToolWindow.Title : base.GetToolWindowTitle(toolWindowType, id);

    protected override Task<object?> InitializeToolWindowAsync(Type toolWindowType, int id, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(reader);

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = await GetServiceAsync(typeof(SDTE)) as DTE
            ?? throw new InvalidOperationException("The DTE service is not available.");
        reader = new PlcTreeReader(dte);
        mcpHost = CodeTwinMcpHost.Start(new UiThreadPlcAutomation(JoinableTaskFactory, reader));

        SolutionEvents.OnAfterBackgroundSolutionLoadComplete += OnSolutionLoaded;
        SolutionEvents.OnAfterCloseSolution += OnSolutionClosed;
    }

    // Creating the tool window frame waits for this package to finish loading, so the initial show must run after InitializeAsync.
    protected override Task OnAfterPackageLoadedAsync(CancellationToken cancellationToken) =>
        ShowPlcTreeAsync(cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SolutionEvents.OnAfterBackgroundSolutionLoadComplete -= OnSolutionLoaded;
            SolutionEvents.OnAfterCloseSolution -= OnSolutionClosed;
            mcpHost?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnSolutionLoaded(object sender, EventArgs e) =>
        JoinableTaskFactory.RunAsync(() => ShowPlcTreeAsync(DisposalToken)).FileAndForget("CodeTwin/ShowPlcTree");

    private void OnSolutionClosed(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (FindToolWindow(typeof(PlcTreeToolWindow), 0, create: false) is PlcTreeToolWindow window)
        {
            window.Control.Clear();
        }
    }

    private async Task ShowPlcTreeAsync(CancellationToken cancellationToken)
    {
        ToolWindowPane pane = await ShowToolWindowAsync(typeof(PlcTreeToolWindow), 0, create: true, cancellationToken);
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        ((PlcTreeToolWindow)pane).Control.Refresh();
    }
}
