using System;
using System.Collections.Generic;

namespace CodeTwin.Core;

public sealed class PlcElement
{
    public PlcElement(string projectName, string name, string path, string itemType, string? declaration, string? implementation)
    {
        ProjectName = projectName;
        Name = name;
        Path = path;
        ItemType = itemType;
        Declaration = declaration;
        Implementation = implementation;
    }

    public string ProjectName { get; }
    public string Name { get; }
    public string Path { get; }
    public string ItemType { get; }
    public string? Declaration { get; }
    public string? Implementation { get; }
}

public interface IPlcAutomation
{
    IReadOnlyList<PlcElement> ListElements();
    PlcElement? ReadElement(string path);
    IReadOnlyList<PlcElement> SearchElements(string query);
}

public interface IPlcAutomationWriter : IPlcAutomation
{
    PlcElement WriteElement(string path, string? declaration, string? implementation);
    PlcReplaceResult ReplaceElementText(string path, string find, string replace, PlcTextScope scope, bool replaceAll);
    void Save();
    PlcElement CreateElement(PlcCreateRequest request);
    PlcBuildResult Build();
    IReadOnlyList<PlcDiagnostic> GetDiagnostics();
}

public sealed class PlcDiagnostic
{
    public PlcDiagnostic(string severity, string description, string? fileName, int line, int column, string? project)
    {
        Severity = severity;
        Description = description;
        FileName = fileName;
        Line = line;
        Column = column;
        Project = project;
    }

    public string Severity { get; }
    public string Description { get; }
    public string? FileName { get; }
    public int Line { get; }
    public int Column { get; }
    public string? Project { get; }
}

public sealed class PlcBuildResult
{
    public PlcBuildResult(bool succeeded, IReadOnlyList<PlcDiagnostic> diagnostics)
    {
        Succeeded = succeeded;
        Diagnostics = diagnostics;
    }

    public bool Succeeded { get; }
    public IReadOnlyList<PlcDiagnostic> Diagnostics { get; }
}

public enum PlcElementKind
{
    Program,
    FunctionBlock,
    Function,
    Method,
    Property,
    Struct,
    Enum,
    GlobalVariableList,
}

public sealed class PlcCreateRequest
{
    public PlcCreateRequest(string parentPath, string name, PlcElementKind kind, string? returnType, string? declaration, string? implementation)
    {
        ParentPath = parentPath;
        Name = name;
        Kind = kind;
        ReturnType = returnType;
        Declaration = declaration;
        Implementation = implementation;
    }

    public string ParentPath { get; }
    public string Name { get; }
    public PlcElementKind Kind { get; }
    public string? ReturnType { get; }
    public string? Declaration { get; }
    public string? Implementation { get; }
}

public enum PlcTextScope
{
    Declaration,
    Implementation,
    Both,
}

public sealed class PlcReplaceResult
{
    public PlcReplaceResult(PlcElement element, int replacementCount)
    {
        Element = element;
        ReplacementCount = replacementCount;
    }

    public PlcElement Element { get; }
    public int ReplacementCount { get; }
}
