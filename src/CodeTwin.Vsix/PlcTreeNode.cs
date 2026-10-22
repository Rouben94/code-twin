using System.Collections.Generic;
using System.Linq;

namespace CodeTwin.Vsix;

internal sealed class PlcTreeNode(string name, string itemType, IReadOnlyList<PlcTreeNode> children)
{
    public string Name { get; } = name;

    public string ItemType { get; } = itemType;

    public IReadOnlyList<PlcTreeNode> Children { get; } = children;

    public int CountNodes() => 1 + Children.Sum(child => child.CountNodes());
}
