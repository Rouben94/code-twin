using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeTwin.Core;

public static class McpToolCatalog
{
    internal static readonly IReadOnlyDictionary<string, PlcElementKind> ElementKinds = new Dictionary<string, PlcElementKind>(StringComparer.Ordinal)
    {
        ["folder"] = PlcElementKind.Folder,
        ["program"] = PlcElementKind.Program,
        ["functionBlock"] = PlcElementKind.FunctionBlock,
        ["function"] = PlcElementKind.Function,
        ["method"] = PlcElementKind.Method,
        ["property"] = PlcElementKind.Property,
        ["propertyGet"] = PlcElementKind.PropertyGet,
        ["propertySet"] = PlcElementKind.PropertySet,
        ["struct"] = PlcElementKind.Struct,
        ["enum"] = PlcElementKind.Enum,
        ["gvl"] = PlcElementKind.GlobalVariableList,
        ["interface"] = PlcElementKind.Interface,
        ["union"] = PlcElementKind.Union,
        ["alias"] = PlcElementKind.Alias,
        ["action"] = PlcElementKind.Action,
    };

    public static object[] GetTools(bool includeWriteTools)
    {
        var tools = new List<object>
        {
            new
            {
                name = "plc_list_elements",
                description = "List PLC elements in the open TwinCAT solution.",
                inputSchema = new { type = "object", properties = new { } },
            },
            new
            {
                name = "plc_read_element",
                description = "Read a PLC element declaration and implementation.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { path = new { type = "string" } },
                    required = new[] { "path" },
                },
            },
            new
            {
                name = "plc_search_elements",
                description = "Search PLC element names, paths, types, and source text.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { query = new { type = "string" } },
                    required = new[] { "query" },
                },
            },
        };

        if (includeWriteTools)
        {
            tools.Add(new
            {
                name = "plc_write_element",
                description = "Replace the declaration and/or implementation of a PLC element. The PLC editor remains open.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        declaration = new { type = "string" },
                        implementation = new { type = "string" },
                    },
                    required = new[] { "path" },
                },
            });
            tools.Add(new
            {
                name = "plc_save_solution",
                description = "Save the open TwinCAT solution.",
                inputSchema = new { type = "object", properties = new { } },
            });
            tools.Add(new
            {
                name = "plc_replace_element_text",
                description = "Find and replace text in a PLC element declaration and/or implementation. The PLC editor remains open.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        find = new { type = "string" },
                        replace = new { type = "string" },
                        scope = new { type = "string", @enum = new[] { "declaration", "implementation", "both" } },
                        replaceAll = new { type = "boolean" },
                    },
                    required = new[] { "path", "find", "replace", "scope", "replaceAll" },
                },
            });
            tools.Add(new
            {
                name = "plc_create_element",
                description = "Create a folder, Structured Text POU, interface, method, property, property accessor, action, DUT (struct, enum, union, alias), or GVL below a PLC element (e.g. the PLC project, a folder or a function block). "
                    + "returnType is required for function and property and optional for method. A new property has no accessors: add them with kind propertyGet / propertySet and the property as parentPath; "
                    + "name is optional for accessors (always Get / Set). Edit an existing accessor at '<property path>/Get' or '<property path>/Set'. "
                    + "Methods and properties below an interface become interface members (declaration only). An action belongs to a program or function block and has only an implementation. "
                    + "An alias needs its declaration (e.g. 'TYPE T_Speed : LREAL;\\nEND_TYPE'). "
                    + "Optional declaration and implementation replace the generated text; folders take neither.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        parentPath = new { type = "string" },
                        name = new { type = "string" },
                        kind = new { type = "string", @enum = ElementKinds.Keys.ToArray() },
                        returnType = new { type = "string" },
                        declaration = new { type = "string" },
                        implementation = new { type = "string" },
                    },
                    required = new[] { "parentPath", "kind" },
                },
            });
            tools.Add(new
            {
                name = "plc_rename_element",
                description = "Rename a PLC element (POU, method, property, DUT, GVL, folder, ...). TwinCAT updates the element's own declaration header and file name, "
                    + "but not the code that uses the old name: search for it with plc_search_elements, update every use, then plc_build.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        newName = new { type = "string" },
                    },
                    required = new[] { "path", "newName" },
                },
            });
            tools.Add(new
            {
                name = "plc_delete_element",
                description = "Delete a PLC element and everything below it (a folder with its content, a POU with its methods and properties). "
                    + "Code that uses it is not changed: check with plc_search_elements first and run plc_build afterwards.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { path = new { type = "string" } },
                    required = new[] { "path" },
                },
            });
            tools.Add(new
            {
                name = "plc_build",
                description = "Build the open TwinCAT solution and return whether it succeeded plus the error list. Each diagnostic has Severity (error, warning, message), Code (e.g. C0046), Description, FileName, 1-based Line and Project.",
                inputSchema = new { type = "object", properties = new { } },
            });
            tools.Add(new
            {
                name = "plc_get_errors",
                description = "Read the current error list (errors, warnings, messages) of the TwinCAT shell, including live precompile results. Same diagnostic shape as plc_build.",
                inputSchema = new { type = "object", properties = new { } },
            });
        }

        return tools.ToArray();
    }
}
