using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Linq;
using CodeTwin.Core;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace CodeTwin.Vsix;

/// <summary>Reads the PLC part of every TwinCAT project in the open solution via the Automation Interface.</summary>
internal sealed class PlcTreeReader(DTE dte)
{
    private const string PlcConfigurationPath = "TIPC";

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
        return ListElementsCore().FirstOrDefault(element => string.Equals(element.Path, path, StringComparison.OrdinalIgnoreCase));
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
        (int subType, object? info) = request.Kind switch
        {
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
            PlcElementKind.GlobalVariableList => (615, request.Declaration),
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

        string? declaration = (item as ITcPlcDeclaration)?.DeclarationText;
        string? implementation = (item as ITcPlcImplementation)?.ImplementationText;
        elements.Add(new PlcElement(projectName, item.Name, path, DescribeItemType(item), declaration, implementation));

        foreach (ITcSmTreeItem child in item)
        {
            ReadElements(projectName, child, path + "/" + child.Name, elements);
        }
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
