using System.Runtime.InteropServices;

namespace CodeTwin.Mcp;

/// <summary>Runs ShellExecute inside Explorer, so the started process is a child of Explorer.</summary>
/// <remarks>
/// MCP clients such as Copilot CLI put their servers in a kill-on-close job object that forbids breakaway.
/// A shell started directly by the bridge would be killed, with any unsaved work, when the client exits.
/// </remarks>
internal static class DesktopShell
{
    private const int CsidlDesktop = 0;
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;
    private const uint SvgioBackground = 0;
    private const int SwShowNormal = 1;

    private static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid TopLevelBrowserSid = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid ShellBrowserIid = typeof(IShellBrowser).GUID;
    private static readonly Guid DispatchIid = new("00020400-0000-0000-C000-000000000046");

    public static void Execute(string file, string arguments, string directory)
    {
        var shellWindows = (IShellWindows)(Activator.CreateInstance(Type.GetTypeFromCLSID(ShellWindowsClsid, throwOnError: true)!)
            ?? throw new InvalidOperationException("Explorer is not available."));
        object location = CsidlDesktop;
        object root = new();
        object desktop = shellWindows.FindWindowSW(ref location, ref root, SwcDesktop, out _, SwfoNeedDispatch);
        var browser = (IShellBrowser)((IServiceProvider)desktop).QueryService(TopLevelBrowserSid, ShellBrowserIid);
        IShellView view = browser.QueryActiveShellView();
        dynamic folderView = view.GetItemObject(SvgioBackground, DispatchIid);
        dynamic shell = folderView.Application;
        shell.ShellExecute(file, arguments, directory, "open", SwShowNormal);
    }

    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        void GetCount();

        void Item();

        void NewEnum();

        void Register();

        void RegisterPending();

        void Revoke();

        void OnNavigate();

        void OnActivated();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(
            [MarshalAs(UnmanagedType.Struct)] ref object location,
            [MarshalAs(UnmanagedType.Struct)] ref object root,
            int windowClass,
            out int windowHandle,
            int options);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(in Guid service, in Guid riid);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();

        void ContextSensitiveHelp();

        void InsertMenusSB();

        void SetMenuSB();

        void RemoveMenusSB();

        void SetStatusTextSB();

        void EnableModelessSB();

        void TranslateAcceleratorSB();

        void BrowseObject();

        void GetViewStateStream();

        void GetControlWindow();

        void SendControlMsg();

        IShellView QueryActiveShellView();
    }

    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow();

        void ContextSensitiveHelp();

        void TranslateAccelerator();

        void EnableModeless();

        void UIActivate();

        void Refresh();

        void CreateViewWindow();

        void DestroyViewWindow();

        void GetCurrentInfo();

        void AddPropertySheetPages();

        void SaveViewState();

        void SelectItem();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object GetItemObject(uint item, in Guid riid);
    }
}
