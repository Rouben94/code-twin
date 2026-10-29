using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CodeTwin.Core;
using Microsoft.VisualStudio.Threading;

namespace CodeTwin.Vsix;

/// <summary>Marshals every Automation Interface call from the MCP request thread to the Visual Studio UI thread.</summary>
[SuppressMessage("Usage", "VSTHRD010", Justification = "Run switches to the main thread before invoking each delegate.")]
internal sealed class UiThreadPlcAutomation(JoinableTaskFactory joinableTaskFactory, PlcTreeReader reader, ErrorListReader errorList) : IPlcAutomationWriter
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

    public PlcElement RenameElement(string path, string newName) => Run(() => reader.RenameElement(path, newName));

    public void DeleteElement(string path) => Run(() =>
    {
        reader.DeleteElement(path);
        return true;
    });

    public string ReadVisualization(string path) => Run(() => reader.ReadVisualization(path));

    public void WriteVisualization(string path, string xml) => Run(() =>
    {
        reader.WriteVisualization(path, xml);
        return true;
    });

    public PlcBuildResult Build() =>
        joinableTaskFactory.Run(async () =>
        {
            // TwinCAT never clears its own "build ... failed" log lines, so file-less messages from earlier builds are dropped.
            HashSet<string> earlierMessages =
                [.. (await errorList.ReadAsync(buildOnly: true)).Where(d => d.FileName is null).Select(d => d.Description)];
            await joinableTaskFactory.SwitchToMainThreadAsync();
            bool succeeded = reader.Build();
            IReadOnlyList<PlcDiagnostic> diagnostics = await errorList.ReadAsync(buildOnly: true);
            return new PlcBuildResult(
                succeeded,
                [.. diagnostics.Where(d => d.FileName is not null || !earlierMessages.Contains(d.Description))]);
        });

    public IReadOnlyList<PlcDiagnostic> GetDiagnostics() => joinableTaskFactory.Run(() => errorList.ReadAsync(buildOnly: false));

    private T Run<T>(Func<T> action) =>
        joinableTaskFactory.Run(async () =>
        {
            await joinableTaskFactory.SwitchToMainThreadAsync();
            return action();
        });
}
