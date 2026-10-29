using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using CodeTwin.Core;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace CodeTwin.Vsix;

/// <summary>Reads the PLC part of every TwinCAT project in the open solution via the Automation Interface.</summary>
internal sealed class PlcTreeReader(DTE dte)
{
    private const string PlcConfigurationPath = "TIPC";
    private const int VisualizationImportSubType = 58;

    public IReadOnlyList<PlcElement> ListElements()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return ListElementsCore();
    }

    [SuppressMessage("Usage", "VSTHRD010", Justification = "The public entry points validate the Visual Studio UI thread before calling this helper.")]
    private IReadOnlyList<PlcElement> ListElementsCore()
    {
        List<PlcElement> elements = [];
        foreach (Project project in dte.Solution.Projects)
        {
            if (project.Object is not ITcSysManager sysManager)
            {
                continue;
            }

            ITcSmTreeItem plcConfiguration = sysManager.LookupTreeItem(PlcConfigurationPath);
            ReadElements(project.Name, plcConfiguration, project.Name, elements);
        }

        return elements;
    }

    /// <summary>Finds element paths by element name, reading names only so it stays cheap on large projects.</summary>
    public IReadOnlyList<string> FindElementPaths(string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        List<string> paths = [];
        foreach (Project project in dte.Solution.Projects)
        {
            if (project.Object is ITcSysManager sysManager)
            {
                FindElementPaths(sysManager.LookupTreeItem(PlcConfigurationPath), project.Name, name, paths);
            }
        }

        return paths;
    }

    public PlcElement? ReadElement(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ITcSmTreeItem? item = FindItem(path);
        return item is null ? null : ToPlcElement(path.Substring(0, path.IndexOf('/')), item, path);
    }

    public IReadOnlyList<PlcElement> SearchElements(string query)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        IReadOnlyList<PlcElement> elements = ListElementsCore();
        return new DelegatingPlcAutomation(() => elements).SearchElements(query);
    }

    public PlcElement WriteElement(string path, string? declaration, string? implementation)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ITcSmTreeItem? item = FindItem(path);
        if (item is null)
        {
            throw new KeyNotFoundException($"PLC element '{path}' was not found.");
        }

        if (declaration is not null)
        {
            if (item is not ITcPlcDeclaration declarationItem)
            {
                throw new InvalidOperationException($"PLC element '{path}' has no declaration.");
            }

            declarationItem.DeclarationText = declaration;
        }

        if (implementation is not null)
        {
            if (item is not ITcPlcImplementation implementationItem)
            {
                throw new InvalidOperationException($"PLC element '{path}' has no implementation.");
            }

            implementationItem.ImplementationText = implementation;
        }

        return ReadElement(path) ?? throw new InvalidOperationException($"PLC element '{path}' disappeared after writing.");
    }

    public PlcReplaceResult ReplaceElementText(string path, string find, string replace, PlcTextScope scope, bool replaceAll)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (string.IsNullOrEmpty(find))
        {
            throw new ArgumentException("The search text cannot be empty.", nameof(find));
        }

        ITcSmTreeItem? item = FindItem(path);
        if (item is null)
        {
            throw new KeyNotFoundException($"PLC element '{path}' was not found.");
        }

        ITcPlcDeclaration? declarationItem = item as ITcPlcDeclaration;
        ITcPlcImplementation? implementationItem = item as ITcPlcImplementation;
        string? declaration = declarationItem?.DeclarationText;
        string? implementation = implementationItem?.ImplementationText;
        int replacementCount = 0;

        string? updatedDeclaration = declaration;
        string? updatedImplementation = implementation;
        if (scope is PlcTextScope.Declaration or PlcTextScope.Both)
        {
            if (declarationItem is null)
            {
                throw new InvalidOperationException($"PLC element '{path}' has no declaration.");
            }

            updatedDeclaration = ReplaceText(declaration, find, replace, replaceAll, ref replacementCount);
        }

        if (scope is PlcTextScope.Implementation or PlcTextScope.Both)
        {
            if (implementationItem is null)
            {
                throw new InvalidOperationException($"PLC element '{path}' has no implementation.");
            }

            updatedImplementation = ReplaceText(implementation, find, replace, replaceAll, ref replacementCount);
        }

        if (replacementCount == 0)
        {
            throw new InvalidOperationException($"Text was not found in PLC element '{path}'.");
        }

        if (updatedDeclaration != declaration)
        {
            declarationItem!.DeclarationText = updatedDeclaration;
        }

        if (updatedImplementation != implementation)
        {
            implementationItem!.ImplementationText = updatedImplementation;
        }

        PlcElement element = ReadElement(path)
            ?? throw new InvalidOperationException($"PLC element '{path}' disappeared after replacing text.");
        return new PlcReplaceResult(element, replacementCount);
    }

    public void Save()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        dte.ExecuteCommand("File.SaveAll", string.Empty);
    }

    public PlcElement CreateElement(PlcCreateRequest request)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ITcSmTreeItem parent = FindItem(request.ParentPath)
            ?? throw new KeyNotFoundException($"PLC element '{request.ParentPath}' was not found.");

        const int Language = 1; // IECLANGUAGE_ST
        bool inInterface = (PlcItemType)parent.ItemType is PlcItemType.PLCITF or PlcItemType.PLCITFPROP;
        (int subType, object? info) = request.Kind switch
        {
            PlcElementKind.Method when inInterface => (610, request.ReturnType),
            PlcElementKind.Property when inInterface => (612, request.ReturnType),
            PlcElementKind.PropertyGet when inInterface => (654, null),
            PlcElementKind.PropertySet when inInterface => (655, null),
            PlcElementKind.Folder => (601, null),
            PlcElementKind.Program => (602, (object?)Language),
            PlcElementKind.FunctionBlock => (604, Language),
            PlcElementKind.Function => (603, new object?[] { Language, request.ReturnType }),
            PlcElementKind.Method => (609, new object?[] { Language, request.ReturnType }),
            PlcElementKind.Property => (611, new object?[] { Language, request.ReturnType }),
            PlcElementKind.PropertyGet => (613, null),
            PlcElementKind.PropertySet => (614, null),
            PlcElementKind.Struct => (606, request.Declaration),
            PlcElementKind.Enum => (605, request.Declaration),
            PlcElementKind.GlobalVariableList => (615, request.Declaration ?? string.Empty),
            PlcElementKind.Interface => (618, null),
            PlcElementKind.Union => (607, request.Declaration),
            PlcElementKind.Alias => (623, request.Declaration),
            PlcElementKind.Action => (608, Language),
            PlcElementKind.Visualization => (619, null),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unsupported PLC element kind."),
        };

        ITcSmTreeItem created = parent.CreateChild(request.Name, subType, string.Empty, info);
        if (request.Declaration is not null && created is ITcPlcDeclaration declarationItem)
        {
            declarationItem.DeclarationText = request.Declaration;
        }

        if (request.Implementation is not null)
        {
            if (created is not ITcPlcImplementation implementationItem)
            {
                throw new InvalidOperationException($"PLC element '{request.Name}' has no implementation.");
            }

            implementationItem.ImplementationText = request.Implementation;
        }

        string path = request.ParentPath + "/" + created.Name;
        return ReadElement(path) ?? throw new InvalidOperationException($"PLC element '{path}' was not found after creation.");
    }

    public PlcElement RenameElement(string path, string newName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ITcSmTreeItem item = FindPlcObject(path);
        item.Name = newName;
        string newPath = path.Substring(0, path.LastIndexOf('/') + 1) + item.Name;
        return ReadElement(newPath) ?? throw new InvalidOperationException($"PLC element '{newPath}' was not found after renaming.");
    }

    public void DeleteElement(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ITcSmTreeItem item = FindPlcObject(path);
        ITcSmTreeItem parent = FindItem(path.Substring(0, path.LastIndexOf('/')))
            ?? throw new KeyNotFoundException($"The parent of PLC element '{path}' was not found.");
        parent.DeleteChild(item.Name);
    }

    public string ReadVisualization(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return File.ReadAllText(GetFullPath(FindVisualization(path)));
    }

    /// <summary>
    /// Replaces a visualization with new TcVIS content. The Automation Interface does not expose visualization content
    /// and cannot import over an existing object, so the object is deleted and the new file imported; on failure the original is imported again.
    /// </summary>
    public void WriteVisualization(string path, string xml)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ITcSmTreeItem visualization = FindVisualization(path);
        ITcSmTreeItem parent = FindItem(path.Substring(0, path.LastIndexOf('/')))
            ?? throw new KeyNotFoundException($"The parent of PLC element '{path}' was not found.");
        string name = visualization.Name;

        // DeleteChild also deletes the file, so both copies live outside the project.
        string folder = Path.Combine(Path.GetTempPath(), "CodeTwin", Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(folder, "original", name + ".TcVIS");
        string changed = Path.Combine(folder, name + ".TcVIS");
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        try
        {
            File.Copy(GetFullPath(visualization), backup);
            File.WriteAllText(changed, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            parent.DeleteChild(name);
            bool imported = false;
            try
            {
                parent.CreateChild(name, VisualizationImportSubType, string.Empty, changed);
                imported = FindChild(parent, name) is { ItemType: (int)PlcItemType.PLCVISOBJ };
            }
            finally
            {
                if (!imported)
                {
                    // TwinCAT adds content it cannot read as a plain file item instead of failing.
                    string fileName = Path.GetFileName(changed);
                    if (FindChild(parent, fileName) is not null)
                    {
                        parent.DeleteChild(fileName);
                    }

                    parent.CreateChild(name, VisualizationImportSubType, string.Empty, backup);
                }
            }

            if (!imported)
            {
                throw new InvalidOperationException($"TwinCAT did not accept the TcVIS content for '{path}'. The original visualization was restored.");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ActivityLog.LogWarning(nameof(PlcTreeReader), $"Deleting the temporary folder '{folder}' failed: {exception.Message}");
            }
        }
    }

    /// <summary>Builds the solution and returns whether the build succeeded.</summary>
    public bool Build()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        SolutionBuild solutionBuild = dte.Solution.SolutionBuild;
        solutionBuild.Build(WaitForBuildToFinish: true);
        return solutionBuild.LastBuildInfo == 0;
    }

    private static void ReadElements(string projectName, ITcSmTreeItem item, string path, ICollection<PlcElement> elements)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        elements.Add(ToPlcElement(projectName, item, path));

        foreach (ITcSmTreeItem child in item)
        {
            ReadElements(projectName, child, path + "/" + child.Name, elements);
        }
    }

    // TwinCAT throws native exceptions (SEHException) for some items; one such item must not hide the rest of the project.
    private static PlcElement ToPlcElement(string projectName, ITcSmTreeItem item, string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string? declaration = null;
        string? implementation = null;
        try
        {
            declaration = (item as ITcPlcDeclaration)?.DeclarationText;
            implementation = (item as ITcPlcImplementation)?.ImplementationText;
        }
        catch (ExternalException exception)
        {
            ActivityLog.LogWarning(nameof(PlcTreeReader), $"Reading the source of PLC element '{path}' failed: {exception.GetType().Name}: {exception.Message}");
        }

        return new PlcElement(projectName, item.Name, path, DescribeItemType(item), declaration, implementation);
    }

    private static void FindElementPaths(ITcSmTreeItem item, string path, string name, ICollection<string> paths)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            paths.Add(path);
        }

        foreach (ITcSmTreeItem child in item)
        {
            FindElementPaths(child, path + "/" + child.Name, name, paths);
        }
    }

    [SuppressMessage("Usage", "VSTHRD010", Justification = "The write entry point validates the Visual Studio UI thread before calling this helper.")]
    private ITcSmTreeItem? FindItem(string path)
    {
        foreach (Project project in dte.Solution.Projects)
        {
            if (project.Object is not ITcSysManager sysManager
                || !path.StartsWith(project.Name + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ITcSmTreeItem plcConfiguration = sysManager.LookupTreeItem(PlcConfigurationPath);
            ITcSmTreeItem? item = FindItem(plcConfiguration, project.Name, path);
            if (item is not null)
            {
                return item;
            }
        }

        return null;
    }

    // Projects, PLC nodes and the library manager are not PLC objects: renaming or deleting them would break the solution.
    [SuppressMessage("Usage", "VSTHRD010", Justification = "The write entry points validate the Visual Studio UI thread before calling this helper.")]
    private ITcSmTreeItem FindPlcObject(string path)
    {
        ITcSmTreeItem item = FindItem(path) ?? throw new KeyNotFoundException($"PLC element '{path}' was not found.");
        if (item.ItemType < (int)PlcItemType.PLCFOLDER || item.ItemType == (int)PlcItemType.PLCLIBMAN)
        {
            throw new InvalidOperationException($"'{path}' is not a PLC object inside a PLC project.");
        }

        return item;
    }

    [SuppressMessage("Usage", "VSTHRD010", Justification = "The visualization entry points validate the Visual Studio UI thread before calling this helper.")]
    private ITcSmTreeItem FindVisualization(string path)
    {
        ITcSmTreeItem item = FindPlcObject(path);
        return item.ItemType == (int)PlcItemType.PLCVISOBJ
            ? item
            : throw new InvalidOperationException($"PLC element '{path}' is not a visualization.");
    }

    private static ITcSmTreeItem? FindChild(ITcSmTreeItem parent, string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (ITcSmTreeItem child in parent)
        {
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    private static string GetFullPath(ITcSmTreeItem item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return XDocument.Parse(item.ProduceXml(bRecursive: false))
            .Descendants("VSProperty")
            .FirstOrDefault(property => string.Equals((string?)property.Element("Name"), "FullPath", StringComparison.Ordinal))
            ?.Element("Value")?.Value
            ?? throw new InvalidOperationException($"TwinCAT reported no file for PLC element '{item.Name}'.");
    }

    private static ITcSmTreeItem? FindItem(ITcSmTreeItem item, string path, string targetPath)
    {
        if (string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return item;
        }

        foreach (ITcSmTreeItem child in item)
        {
            ITcSmTreeItem? match = FindItem(child, path + "/" + child.Name, targetPath);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string ReplaceText(string? source, string find, string replace, bool replaceAll, ref int replacementCount)
    {
        if (source is null || source.Length == 0)
        {
            return source ?? string.Empty;
        }

        string text = source;
        if (replaceAll)
        {
            int offset = 0;
            while ((offset = text.IndexOf(find, offset, StringComparison.Ordinal)) >= 0)
            {
                replacementCount++;
                offset += find.Length;
            }

            return text.Replace(find, replace);
        }

        int index = text.IndexOf(find, StringComparison.Ordinal);
        if (index < 0)
        {
            return text;
        }

        replacementCount++;
        return text.Substring(0, index) + replace + text.Substring(index + find.Length);
    }

    private static string DescribeItemType(ITcSmTreeItem item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var itemType = (PlcItemType)item.ItemType;
        string typeName = Enum.IsDefined(typeof(PlcItemType), itemType)
            ? itemType.ToString()
            : item.ItemType.ToString(CultureInfo.InvariantCulture);

        try
        {
            string subTypeName = item.ItemSubTypeName;
            return string.IsNullOrEmpty(subTypeName) ? typeName : $"{typeName} / {subTypeName}";
        }
        catch (COMException)
        {
            return typeName;
        }
    }

    private static string StripPrefix(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) ? value.Substring(prefix.Length) : value;
}
