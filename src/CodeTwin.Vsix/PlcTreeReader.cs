using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using TCatSysManagerLib;

namespace CodeTwin.Vsix;

/// <summary>Reads the PLC part of every TwinCAT project in the open solution via the Automation Interface.</summary>
internal sealed class PlcTreeReader(DTE dte)
{
    private const string PlcConfigurationPath = "TIPC";

    public IReadOnlyList<PlcTreeNode> ReadPlcTrees()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        List<PlcTreeNode> trees = [];
        foreach (Project project in dte.Solution.Projects)
        {
            if (project.Object is not ITcSysManager sysManager)
            {
                continue;
            }

            ITcSmTreeItem plcConfiguration = sysManager.LookupTreeItem(PlcConfigurationPath);
            trees.Add(new PlcTreeNode(project.Name, "TwinCAT project", [ReadItem(plcConfiguration)]));
        }

        return trees;
    }

    private static PlcTreeNode ReadItem(ITcSmTreeItem item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        List<PlcTreeNode> children = [];
        foreach (ITcSmTreeItem child in item)
        {
            children.Add(ReadItem(child));
        }

        return new PlcTreeNode(item.Name, DescribeItemType(item), children);
    }

    private static string DescribeItemType(ITcSmTreeItem item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var itemType = (TREEITEMTYPES)item.ItemType;
        string typeName = Enum.IsDefined(typeof(TREEITEMTYPES), itemType)
            ? StripPrefix(itemType.ToString(), "TREEITEMTYPE_")
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
