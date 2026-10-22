using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeTwin.Core;

public sealed class DelegatingPlcAutomation(Func<IReadOnlyList<PlcElement>> listElements) : IPlcAutomation
{
    private readonly Func<IReadOnlyList<PlcElement>> listElements = listElements;

    public IReadOnlyList<PlcElement> ListElements() => listElements();

    public PlcElement? ReadElement(string path) =>
        ListElements().FirstOrDefault(element => string.Equals(element.Path, path, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<PlcElement> SearchElements(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ListElements();
        }

        return ListElements()
            .Where(element =>
                element.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || element.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || element.ItemType.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (element.Declaration?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (element.Implementation?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
            .ToArray();
    }
}
