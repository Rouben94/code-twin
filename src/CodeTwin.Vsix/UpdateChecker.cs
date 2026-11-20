using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodeTwin.Core;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace CodeTwin.Vsix;

/// <summary>Finds a newer CodeTwin release on GitHub, offers it in an info bar and installs it by running its setup.</summary>
internal sealed class UpdateChecker(AsyncPackage package, UpdateOptionsPage options, Version installed) : IVsInfoBarUIEvents
{
    private const string InstallAction = "install";
    private const string NotesAction = "notes";
    private const string SkipAction = "skip";

    private static readonly HttpClient Http = CreateHttpClient();

    private AvailableUpdate? offered;
    private uint eventsCookie;

    public async Task CheckOnStartupAsync(CancellationToken cancellationToken)
    {
        if (!options.CheckForUpdates)
        {
            return;
        }

        AvailableUpdate? update;
        try
        {
            update = await FindUpdateAsync(cancellationToken);
        }
        catch (Exception exception) when (IsUpdateFailure(exception, cancellationToken))
        {
            ActivityLog.TryLogWarning(nameof(CodeTwinPackage), $"Update check failed: {exception.Message}");
            return;
        }

        if (update is null || string.Equals(update.Version.ToString(), options.SkippedVersion, StringComparison.Ordinal))
        {
            return;
        }

        await ShowInfoBarAsync(update, cancellationToken);
    }

    public async Task CheckNowAsync(CancellationToken cancellationToken)
    {
        AvailableUpdate? update;
        try
        {
            update = await FindUpdateAsync(cancellationToken);
        }
        catch (Exception exception) when (IsUpdateFailure(exception, cancellationToken))
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            ShowMessage($"The update check failed: {exception.Message}", OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }

        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        if (update is null)
        {
            ShowMessage($"CodeTwin {installed} is the latest version.", OLEMSGICON.OLEMSGICON_INFO);
        }
        else if (VsShellUtilities.ShowMessageBox(
            package,
            $"CodeTwin {update.Version} is available (installed: {installed}). Download and install it now?",
            "CodeTwin Update",
            OLEMSGICON.OLEMSGICON_QUERY,
            OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST) == (int)VSConstants.MessageBoxResult.IDYES)
        {
            await InstallAsync(update, cancellationToken);
        }
    }

    public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        infoBarUIElement.Unadvise(eventsCookie);
    }

    public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (offered is not { } update)
        {
            return;
        }

        switch (actionItem.ActionContext as string)
        {
            case InstallAction:
                infoBarUIElement.Close();
                package.JoinableTaskFactory.RunAsync(() => InstallAsync(update, package.DisposalToken)).FileAndForget("CodeTwin/InstallUpdate");
                break;
            case NotesAction:
                VsShellUtilities.OpenSystemBrowser(update.ReleaseUrl);
                break;
            case SkipAction:
                options.SkippedVersion = update.Version.ToString();
                options.SaveSettingsToStorage();
                infoBarUIElement.Close();
                break;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        // Corporate proxies often require the signed-in Windows account.
        var handler = new HttpClientHandler { DefaultProxyCredentials = CredentialCache.DefaultCredentials };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CodeTwin");
        return http;
    }

    private static bool IsUpdateFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or IOException or UnauthorizedAccessException or Win32Exception
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private async Task<AvailableUpdate?> FindUpdateAsync(CancellationToken cancellationToken)
    {
        await TaskScheduler.Default;
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseFeed.LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return ReleaseFeed.FindUpdate(await response.Content.ReadAsStringAsync(), installed);
    }

    private async Task ShowInfoBarAsync(AvailableUpdate update, CancellationToken cancellationToken)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        if (await package.GetServiceAsync(typeof(SVsInfoBarUIFactory)) is not IVsInfoBarUIFactory factory
            || await package.GetServiceAsync(typeof(SVsShell)) is not IVsShell shell
            || ErrorHandler.Failed(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out object hostObject))
            || hostObject is not IVsInfoBarHost host)
        {
            ActivityLog.TryLogWarning(nameof(CodeTwinPackage), $"CodeTwin {update.Version} is available, but the shell has no info bar.");
            return;
        }

        var model = new InfoBarModel(
            [new InfoBarTextSpan($"CodeTwin {update.Version} is available (installed: {installed}).")],
            [
                new InfoBarHyperlink("Install", InstallAction),
                new InfoBarHyperlink("Release notes", NotesAction),
                new InfoBarHyperlink("Skip this version", SkipAction),
            ],
            KnownMonikers.StatusInformation,
            isCloseButtonVisible: true);
        IVsInfoBarUIElement element = factory.CreateInfoBar(model);
        offered = update;
        element.Advise(this, out eventsCookie);
        host.AddInfoBar(element);
    }

    private async Task InstallAsync(AvailableUpdate update, CancellationToken cancellationToken)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dialogFactory = await package.GetServiceAsync(typeof(SVsThreadedWaitDialogFactory)) as IVsThreadedWaitDialogFactory
            ?? throw new InvalidOperationException("The wait dialog service is not available.");
        string setupPath = Path.Combine(Path.GetTempPath(), "CodeTwin", $"CodeTwin-Setup-{update.Version}.exe");
        try
        {
            using (ThreadedWaitDialogHelper.Session session = dialogFactory.StartWaitDialog(
                "CodeTwin Update", new ThreadedWaitDialogProgressData($"Downloading CodeTwin {update.Version}...", isCancelable: true)))
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.UserCancellationToken);
                await DownloadAsync(update.SetupUrl, setupPath, linked.Token);
            }

            Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = true })?.Dispose();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (IsUpdateFailure(exception, cancellationToken))
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            ShowMessage($"The update could not be installed: {exception.Message}", OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }

        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        ShowMessage("The CodeTwin setup has started. Close TwinCAT XAE Shell when the setup asks for it.", OLEMSGICON.OLEMSGICON_INFO);
    }

    private static async Task DownloadAsync(string url, string path, CancellationToken cancellationToken)
    {
        await TaskScheduler.Default;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using Stream source = await response.Content.ReadAsStreamAsync();
        using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(target, 81920, cancellationToken);
    }

    private void ShowMessage(string message, OLEMSGICON icon)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(package, message, "CodeTwin Update", icon, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
