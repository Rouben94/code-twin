using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace CodeTwin.Vsix;

internal sealed class PlcTreeControl : UserControl
{
    private const int InitiallyExpandedDepth = 3;

    private readonly PlcTreeReader reader;
    private readonly TextBlock status = new() { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TreeView tree = new() { BorderThickness = new Thickness(0) };

    public PlcTreeControl(PlcTreeReader reader)
    {
        this.reader = reader;

        var refreshButton = new Button { Content = "Refresh", Padding = new Thickness(8, 1, 8, 1) };
        refreshButton.Click += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Refresh();
        };

        var toolbar = new DockPanel { Margin = new Thickness(4) };
        toolbar.Children.Add(refreshButton);
        toolbar.Children.Add(status);
        DockPanel.SetDock(toolbar, Dock.Top);

        var layout = new DockPanel();
        layout.Children.Add(toolbar);
        layout.Children.Add(tree);
        Content = layout;

        SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        tree.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        tree.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
    }

    public void Refresh()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        tree.Items.Clear();

        try
        {
            var stopwatch = Stopwatch.StartNew();
            IReadOnlyList<PlcTreeNode> plcTrees = reader.ReadPlcTrees();
            stopwatch.Stop();

            int nodeCount = 0;
            foreach (PlcTreeNode plcTree in plcTrees)
            {
                tree.Items.Add(CreateItem(plcTree, depth: 0));
                nodeCount += plcTree.CountNodes();
            }

            status.Text = plcTrees.Count == 0
                ? "No TwinCAT project in the open solution."
                : string.Format(CultureInfo.InvariantCulture, "{0} nodes read in {1} ms", nodeCount, stopwatch.ElapsedMilliseconds);
            ActivityLog.LogInformation(nameof(CodeTwinPackage), status.Text);
        }
        catch (Exception ex)
        {
            ActivityLog.LogError(nameof(CodeTwinPackage), ex.ToString());
            status.Text = $"Reading the PLC tree failed: {ex.Message}";
        }
    }

    public void Clear()
    {
        tree.Items.Clear();
        status.Text = "No solution open.";
    }

    private static TreeViewItem CreateItem(PlcTreeNode node, int depth)
    {
        var item = new TreeViewItem
        {
            Header = $"{node.Name}  [{node.ItemType}]",
            IsExpanded = depth < InitiallyExpandedDepth,
        };

        foreach (PlcTreeNode child in node.Children)
        {
            item.Items.Add(CreateItem(child, depth + 1));
        }

        return item;
    }
}
