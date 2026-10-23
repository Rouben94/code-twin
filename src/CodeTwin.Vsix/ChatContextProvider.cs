using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace CodeTwin.Vsix;

internal sealed class ChatContextProvider(DTE dte, PlcTreeReader reader)
{
    public string? GetSolution()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string solution = dte.Solution?.FullName ?? string.Empty;
        return solution.Length == 0 ? null : solution;
    }

    public string? GetActiveElement()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            string? file = dte.ActiveDocument?.FullName;
            if (string.IsNullOrEmpty(file))
            {
                return null;
            }

            IReadOnlyList<string> paths = reader.FindElementPaths(Path.GetFileNameWithoutExtension(file));
            return paths.Count == 1 ? paths[0] : $"{Path.GetFileName(file)} ({file})";
        }
        catch (COMException exception)
        {
            ActivityLog.LogWarning(nameof(CodeTwinPackage), $"Reading the active document failed: {exception.Message}");
            return null;
        }
    }
}
