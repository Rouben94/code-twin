using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private readonly ChatOptionsPage options;
    private string? provider;
    private readonly WebView2 webView = new();
    private bool initialized;

    public ChatControl(ChatServices services)
    {
        context = services.Context;
        joinableTaskFactory = services.JoinableTaskFactory;
        options = services.Options;
        sidecar = new ChatSidecar(Path.Combine(ExtensionDirectory, "chat", "code-twin-chat.exe"), services.McpEndpoint, services.McpToken, options.SidecarEnvironment);
        sidecar.MessageReceived += OnSidecarMessage;
        ChatOptionsPage.Applied += OnOptionsApplied;
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
        ChatOptionsPage.Applied -= OnOptionsApplied;
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
                provider ??= string.Join(";", options.SidecarEnvironment().Values);
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
                    attachments = root.GetProperty("attachments"),
                    solution,
                    activeElement,
                }));
                break;
            case "attachFiles":
                // Files picked or dropped in the page arrive as CoreWebView2File objects that carry their path on disk.
                Post(new { type = "attached", files = e.AdditionalObjects.OfType<CoreWebView2File>().Select(file => file.Path).Where(path => !string.IsNullOrEmpty(path)) });
                break;
            case "attachData":
                string name = root.GetProperty("name").GetString() ?? string.Empty;
                byte[] data = Convert.FromBase64String(root.GetProperty("data").GetString() ?? string.Empty);
                joinableTaskFactory.RunAsync(() => SavePastedFileAsync(name, data)).FileAndForget("CodeTwin/AttachPastedFile");
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

    // A pasted screenshot has no file on disk, so it is saved to the temp folder and attached by that path.
    private async Task SavePastedFileAsync(string name, byte[] data)
    {
        string fileName = string.Concat(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0 ? c : '_'));
        string folder = Path.Combine(Path.GetTempPath(), "CodeTwin", "Attachments", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, string.IsNullOrWhiteSpace(fileName) ? "pasted.png" : fileName);
        Directory.CreateDirectory(folder);
        using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await stream.WriteAsync(data, 0, data.Length);
        }

        await joinableTaskFactory.SwitchToMainThreadAsync();
        Post(new { type = "attached", files = new[] { path } });
    }

    // Only a provider change restarts the chat; saving the options unchanged keeps the running conversation.
    private void OnOptionsApplied(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string current = string.Join(";", options.SidecarEnvironment().Values);
        if (provider is not null && provider != current)
        {
            Post(new { type = "providerChanged" });
            sidecar.Restart();
        }

        provider = current;
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
