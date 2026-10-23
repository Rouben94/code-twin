using System.Collections.Generic;
using System.Threading.Tasks;
using CodeTwin.Core;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.Threading;

namespace CodeTwin.Vsix;

/// <summary>Reads diagnostics from the Error List table, which carries the real severity and the TwinCAT Cxxxx code.</summary>
internal sealed class ErrorListReader(JoinableTaskFactory joinableTaskFactory, IErrorList errorList)
{
    // Flushes pending table updates first, so entries from a build that just finished are complete and stale ones are gone.
    // buildOnly skips the background precompile entries, which can be stale and do not affect the build result.
    public async Task<IReadOnlyList<PlcDiagnostic>> ReadAsync(bool buildOnly)
    {
        await joinableTaskFactory.SwitchToMainThreadAsync();
        EntriesChangedEventArgs update = await errorList.TableControl.ForceUpdateAsync();
        await joinableTaskFactory.SwitchToMainThreadAsync();

        List<PlcDiagnostic> diagnostics = [];
        HashSet<(string, string?, string, string?, int, int)> seen = [];
        foreach (ITableEntryHandle entry in update.AllEntries)
        {
            if (buildOnly && Read(entry, StandardTableKeyNames.ErrorSource) is not ErrorSource.Build)
            {
                continue;
            }

            PlcDiagnostic diagnostic = ReadEntry(entry);
            // The build and the background precompile report the same issue as separate entries.
            if (seen.Add((diagnostic.Severity, diagnostic.Code, diagnostic.Description, diagnostic.FileName, diagnostic.Line, diagnostic.Column)))
            {
                diagnostics.Add(diagnostic);
            }
        }

        return diagnostics;
    }

    private static PlcDiagnostic ReadEntry(ITableEntryHandle entry)
    {
        string severity = Read(entry, StandardTableKeyNames.ErrorSeverity) switch
        {
            __VSERRORCATEGORY.EC_ERROR => "error",
            __VSERRORCATEGORY.EC_WARNING => "warning",
            _ => "message",
        };

        return new PlcDiagnostic(
            severity,
            Read(entry, StandardTableKeyNames.Text) as string ?? string.Empty,
            NullIfEmpty(Read(entry, StandardTableKeyNames.DocumentName) as string),
            ToOneBased(Read(entry, StandardTableKeyNames.Line)),
            ToOneBased(Read(entry, StandardTableKeyNames.Column)),
            NullIfEmpty(Read(entry, StandardTableKeyNames.ProjectName) as string),
            NullIfEmpty(Read(entry, StandardTableKeyNames.ErrorCode) as string));
    }

    private static object? Read(ITableEntryHandle entry, string key) =>
        entry.TryGetValue(key, out object? value) ? value : null;

    // The table is 0-based and uses -1 for "no location"; diagnostics are 1-based with 0 for "no location".
    private static int ToOneBased(object? value) => value is int index && index >= 0 ? index + 1 : 0;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
