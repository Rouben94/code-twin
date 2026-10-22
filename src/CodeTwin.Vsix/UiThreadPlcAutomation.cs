using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CodeTwin.Core;
using Microsoft.VisualStudio.Threading;

namespace CodeTwin.Vsix;

/// <summary>Marshals every Automation Interface call from the MCP request thread to the Visual Studio UI thread.</summary>
[SuppressMessage("Usage", "VSTHRD010", Justification = "Run switches to the main thread before invoking each delegate.")]
internal sealed class UiThreadPlcAutomation(JoinableTaskFactory joinableTaskFactory, PlcTreeReader reader) : IPlcAutomationWriter
{
    public IReadOnlyList<PlcElement> ListElements() => Run(reader.ListElements);

    public PlcElement? ReadElement(string path) => Run(() => reader.ReadElement(path));

    public IReadOnlyList<PlcElement> SearchElements(string query) => Run(() => reader.SearchElements(query));

    public PlcElement WriteElement(string path, string? declaration, string? implementation) =>
        Run(() => reader.WriteElement(path, declaration, implementation));

    public PlcReplaceResult ReplaceElementText(string path, string find, string replace, PlcTextScope scope, bool replaceAll) =>
        Run(() => reader.ReplaceElementText(path, find, replace, scope, replaceAll));

    public void Save() => Run(() =>
    {
        reader.Save();
        return true;
    });

    public PlcElement CreateElement(PlcCreateRequest request) => Run(() => reader.CreateElement(request));

    public PlcBuildResult Build() => Run(reader.Build);

    public IReadOnlyList<PlcDiagnostic> GetDiagnostics() => Run(reader.GetDiagnostics);

    private T Run<T>(Func<T> action) =>
        joinableTaskFactory.Run(async () =>
        {
            await joinableTaskFactory.SwitchToMainThreadAsync();
            return action();
        });
}
