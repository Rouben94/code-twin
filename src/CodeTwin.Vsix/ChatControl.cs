using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Task = System.Threading.Tasks.Task;

namespace CodeTwin.Vsix;

internal sealed class ChatControl : UserControl, IDisposable
{
    private const string HostName = "codetwin.chat";
    private static readonly string ExtensionDirectory = Path.GetDirectoryName(typeof(ChatControl).Assembly.Location)!;

    private readonly ChatContextProvider context;
    private readonly JoinableTaskFactory joinableTaskFactory;
    private readonly ChatSidecar sidecar;
    private readonly WebView2 webView = new();
    private bool initialized;

    public ChatControl(ChatServices services)
    {
        context = services.Context;
        joinableTaskFactory = services.JoinableTaskFactory;
        sidecar = new ChatSidecar(Path.Combine(ExtensionDirectory, "chat", "code-twin-chat.exe"), services.McpEndpoint, services.McpToken);
        sidecar.MessageReceived += OnSidecarMessage;
        VSColorTheme.ThemeChanged += OnThemeChanged;

        Content = webView;
        Loaded += (_, _) =>
        {
            if (!initialized)
            {
                initialized = true;
                joinableTaskFactory.RunAsync(InitializeWebViewAsync).FileAndForget("CodeTwin/InitializeChat");
            }
        };
    }

    public void Dispose()
    {
        VSColorTheme.ThemeChanged -= OnThemeChanged;
        sidecar.MessageReceived -= OnSidecarMessage;
        sidecar.Dispose();
        webView.Dispose();
    }

    private async Task InitializeWebViewAsync()
    {
        await joinableTaskFactory.SwitchToMainThreadAsync();
        try
        {
            try
            {
                // The shell ships the managed WebView2 assemblies but not the native loader, so point them at ours.
                CoreWebView2Environment.SetLoaderDllFolderPath(Path.Combine(ExtensionDirectory, "runtimes", "win-x64", "native"));
            }
            catch (InvalidOperationException)
            {
                // Another extension loaded WebView2 first; its loader works just as well.
            }

            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeTwin", "WebView2");
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            webView.DefaultBackgroundColor = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            await webView.EnsureCoreWebView2Async(environment);

            CoreWebView2 core = webView.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(HostName, Path.Combine(ExtensionDirectory, "ChatUi"), CoreWebView2HostResourceAccessKind.Deny);
            core.Settings.IsStatusBarEnabled = false;
            core.WebMessageReceived += OnWebMessage;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.Navigate($"https://{HostName}/index.html");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Content = new TextBlock { Text = "The CodeTwin chat needs the Microsoft Edge WebView2 Runtime.", Margin = new System.Windows.Thickness(8) };
        }
    }

    private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string message = e.TryGetWebMessageAsString();
        using JsonDocument document = JsonDocument.Parse(message);
        JsonElement root = document.RootElement;
        string? type = root.GetProperty("type").GetString();
        switch (type)
        {
            case "ready":
                PostTheme();
                PostContext();
                sidecar.EnsureStarted();
                break;
            case "getContext":
                PostContext();
                break;
            case "send":
                string? solution = context.GetSolution();
                string? activeElement = context.GetActiveElement();
                Post(new { type = "context", solution, activeElement });
                sidecar.Send(JsonSerializer.Serialize(new
                {
                    type = "send",
                    prompt = root.GetProperty("prompt").GetString(),
                    solution,
                    activeElement,
                }));
                break;
            case "listSessions":
            case "resumeSession":
            case "deleteSession":
                sidecar.Send(JsonSerializer.Serialize(new
                {
                    type,
                    id = root.TryGetProperty("id", out JsonElement id) ? id.GetString() : null,
                    solution = context.GetSolution(),
                }));
                break;
            default:
                sidecar.Send(message);
                break;
        }
    }

    private void OnSidecarMessage(string line) =>
        joinableTaskFactory.RunAsync(async () =>
        {
            await joinableTaskFactory.SwitchToMainThreadAsync();
            webView.CoreWebView2?.PostWebMessageAsJson(line);
        }).FileAndForget("CodeTwin/ChatMessage");

    private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!e.Uri.StartsWith($"https://{HostName}/", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            OpenExternally(e.Uri);
        }
    }

    private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternally(e.Uri);
    }

    private static void OpenExternally(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? target) && (target.Scheme == Uri.UriSchemeHttps || target.Scheme == Uri.UriSchemeHttp))
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }

    private void OnThemeChanged(ThemeChangedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        webView.DefaultBackgroundColor = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
        PostTheme();
    }

    private void PostTheme()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Post(new
        {
            type = "theme",
            colors = new
            {
                background = ToCss(EnvironmentColors.ToolWindowBackgroundColorKey),
                foreground = ToCss(EnvironmentColors.ToolWindowTextColorKey),
                border = ToCss(EnvironmentColors.ToolWindowBorderColorKey),
                inputBackground = ToCss(EnvironmentColors.ComboBoxBackgroundColorKey),
                inputForeground = ToCss(EnvironmentColors.ComboBoxTextColorKey),
                inputBorder = ToCss(EnvironmentColors.ComboBoxBorderColorKey),
                accent = ToCss(EnvironmentColors.SystemHighlightColorKey),
                accentForeground = ToCss(EnvironmentColors.SystemHighlightTextColorKey),
                muted = ToCss(EnvironmentColors.SystemGrayTextColorKey),
                link = ToCss(EnvironmentColors.ControlLinkTextColorKey),
            },
        });
    }

    private void PostContext()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Post(new { type = "context", solution = context.GetSolution(), activeElement = context.GetActiveElement() });
    }

    private void Post(object message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        webView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    private static string ToCss(ThemeResourceKey key)
    {
        Color color = VSColorTheme.GetThemedColor(key);
        return string.Format(CultureInfo.InvariantCulture, "rgba({0},{1},{2},{3:0.###})", color.R, color.G, color.B, color.A / 255.0);
    }
}
