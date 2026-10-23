using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CodeTwin.Mcp;

/// <summary>Asks a running TcXaeShell (or Visual Studio) which solution it has open, through its DTE in the Running Object Table.</summary>
internal static class RunningSolutions
{
    private const int CallRejected = unchecked((int)0x80010001);
    private const int ServerCallRetryLater = unchecked((int)0x8001010A);
    private const int MaxAttempts = 20;

    public static async Task<string?> GetOpenSolutionAsync(int pid, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return GetOpenSolution(pid);
            }
            catch (COMException exception) when (exception.HResult is CallRejected or ServerCallRetryLater && attempt < MaxAttempts)
            {
                // The shell's UI thread is busy (loading, building); DTE calls are rejected until it is idle.
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string? GetOpenSolution(int pid)
    {
        object? dte = FindDte(pid);
        if (dte is null)
        {
            return null;
        }

        try
        {
            dynamic automation = dte;
            string? fullName = automation.Solution.FullName;
            return string.IsNullOrEmpty(fullName) ? null : Path.GetFullPath(fullName);
        }
        finally
        {
            Marshal.ReleaseComObject(dte);
        }
    }

    private static object? FindDte(int pid)
    {
        string suffix = ":" + pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out IRunningObjectTable table));
        Marshal.ThrowExceptionForHR(CreateBindCtx(0, out IBindCtx bindContext));
        table.EnumRunning(out IEnumMoniker monikers);
        var moniker = new IMoniker[1];
        while (monikers.Next(1, moniker, IntPtr.Zero) == 0)
        {
            moniker[0].GetDisplayName(bindContext, null, out string name);
            if (name.StartsWith('!') && name.Contains(".DTE.", StringComparison.Ordinal) && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                table.GetObject(moniker[0], out object dte);
                return dte;
            }
        }

        return null;
    }

#pragma warning disable SYSLIB1054 // COM interface marshalling needs the built-in marshaller.
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx context);
#pragma warning restore SYSLIB1054
}
