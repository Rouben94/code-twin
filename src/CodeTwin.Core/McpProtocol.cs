using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeTwin.Core;

public sealed class InProcessMcpServer : IDisposable
{
    private static readonly IReadOnlyDictionary<string, PlcElementKind> ElementKinds = new Dictionary<string, PlcElementKind>(StringComparer.Ordinal)
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
    };

    private readonly HttpListener listener = new();
    private readonly IPlcAutomation automation;
    private readonly string token;
    private readonly string endpoint;
    private readonly CancellationTokenSource cancellation = new();

    public InProcessMcpServer(IPlcAutomation automation, string token, int port)
    {
        this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
        this.token = string.IsNullOrWhiteSpace(token)
            ? throw new ArgumentException("A token is required.", nameof(token))
            : token;
        endpoint = $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/mcp/";
        listener.Prefixes.Add(endpoint);
    }

    public string Endpoint => endpoint;

    public void Start()
    {
        if (listener.IsListening)
        {
            return;
        }

        listener.Start();
        _ = Task.Run(AcceptRequestsAsync);
    }

    public void Dispose()
    {
        cancellation.Cancel();
        listener.Close();
        cancellation.Dispose();
    }

    private async Task AcceptRequestsAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellation.IsCancellationRequested)
            {
                return;
            }

            _ = Task.Run(() => HandleRequestAsync(context), cancellation.Token);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            string? path = context.Request.Url?.AbsolutePath;
            if (!string.Equals(path, "/mcp", StringComparison.Ordinal) && !string.Equals(path, "/mcp/", StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                context.Response.AddHeader("Allow", "POST");
                return;
            }

            if (!IsAllowedOrigin(context.Request.Headers["Origin"]))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }

            if (!string.Equals(context.Request.Headers["Authorization"], $"Bearer {token}", StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                return;
            }

            using JsonDocument document = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: cancellation.Token).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("id", out _))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return;
            }

            JsonElement response = HandleMessage(document.RootElement);
            byte[] payload = Encoding.UTF8.GetBytes(response.GetRawText());
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, 0, payload.Length, cancellation.Token).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        catch (ArgumentException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("CodeTwin MCP request failed: {0}", exception);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
    }

    private JsonElement HandleMessage(JsonElement request)
    {
        object? id = request.TryGetProperty("id", out JsonElement requestId) ? requestId : null;
        if (!request.TryGetProperty("jsonrpc", out JsonElement version)
            || !string.Equals(version.GetString(), "2.0", StringComparison.Ordinal)
            || !request.TryGetProperty("method", out JsonElement methodValue))
        {
            return CreateErrorResponse(id, -32600, "Invalid JSON-RPC request.");
        }

        string method = methodValue.GetString() ?? string.Empty;
        try
        {
            object result = method switch
            {
                "initialize" => (object)new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "code-twin", version = "0.1.0" },
                },
                "ping" => new { },
                "tools/list" => new
                {
                    tools = GetTools(),
                },
                "tools/call" => CallTool(request),
                _ => throw new InvalidOperationException($"Unsupported MCP method '{method}'."),
            };

            return CreateResponse(id, result);
        }
        catch (InvalidOperationException exception)
        {
            return CreateErrorResponse(id, -32601, exception.Message);
        }
        catch (KeyNotFoundException exception)
        {
            return CreateErrorResponse(id, -32602, exception.Message);
        }
    }

    private object[] GetTools()
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

        if (automation is IPlcAutomationWriter)
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
                description = "Create a folder, Structured Text POU, method, property, property accessor, DUT, or GVL below a PLC element (e.g. the PLC project, a folder or a function block). "
                    + "returnType is required for function and property and optional for method. A new property has no accessors: add them with kind propertyGet / propertySet and the property as parentPath; "
                    + "name is optional for accessors (always Get / Set). Edit an existing accessor at '<property path>/Get' or '<property path>/Set'. "
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

    private object CallTool(JsonElement request)
    {
        JsonElement parameters = request.GetProperty("params");
        string name = parameters.GetProperty("name").GetString() ?? string.Empty;
        JsonElement arguments = parameters.TryGetProperty("arguments", out JsonElement value)
            ? value
            : JsonSerializer.SerializeToElement(new { });

        Func<object> handler = name switch
        {
            "plc_list_elements" => () => automation.ListElements(),
            "plc_read_element" => () => ReadElement(arguments),
            "plc_search_elements" => () => automation.SearchElements(arguments.GetProperty("query").GetString() ?? string.Empty),
            "plc_write_element" => () => WriteElement(arguments),
            "plc_save_solution" => SaveSolution,
            "plc_replace_element_text" => () => ReplaceElementText(arguments),
            "plc_create_element" => () => CreateElement(arguments),
            "plc_build" => () => RequireWriter().Build(),
            "plc_get_errors" => () => RequireWriter().GetDiagnostics(),
            _ => throw new KeyNotFoundException($"Unknown MCP tool '{name}'."),
        };

        object content;
        try
        {
            content = handler();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or COMException)
        {
            return ToolError(exception.Message);
        }
        catch (Exception exception)
        {
            // Automation Interface calls can fail with arbitrary COM-mapped exceptions; report them to the agent instead of failing the request.
            Trace.TraceError("CodeTwin tool '{0}' failed: {1}", name, exception);
            return ToolError($"{exception.GetType().Name}: {exception.Message}");
        }

        string text = JsonSerializer.Serialize(content);
        return new
        {
            content = new[] { new { type = "text", text } },
            structuredContent = content,
        };
    }

    private object ReadElement(JsonElement arguments)
    {
        string path = arguments.GetProperty("path").GetString() ?? string.Empty;
        PlcElement? element = automation.ReadElement(path);
        return element is null
            ? new { error = "PLC element was not found.", path }
            : element;
    }

    private object WriteElement(JsonElement arguments)
    {
        if (automation is not IPlcAutomationWriter writer)
        {
            throw new InvalidOperationException("This CodeTwin instance is read-only.");
        }

        string path = arguments.GetProperty("path").GetString() ?? string.Empty;
        string? declaration = arguments.TryGetProperty("declaration", out JsonElement declarationValue)
            ? declarationValue.GetString()
            : null;
        string? implementation = arguments.TryGetProperty("implementation", out JsonElement implementationValue)
            ? implementationValue.GetString()
            : null;
        return writer.WriteElement(path, declaration, implementation);
    }

    private object SaveSolution()
    {
        if (automation is not IPlcAutomationWriter writer)
        {
            throw new InvalidOperationException("This CodeTwin instance is read-only.");
        }

        writer.Save();
        return new { saved = true };
    }

    private object ReplaceElementText(JsonElement arguments)
    {
        if (automation is not IPlcAutomationWriter writer)
        {
            throw new InvalidOperationException("This CodeTwin instance is read-only.");
        }

        string path = arguments.GetProperty("path").GetString() ?? string.Empty;
        string find = arguments.GetProperty("find").GetString() ?? string.Empty;
        string replace = arguments.GetProperty("replace").GetString() ?? string.Empty;
        string scope = arguments.GetProperty("scope").GetString() ?? string.Empty;
        bool replaceAll = arguments.GetProperty("replaceAll").GetBoolean();
        if (find.Length == 0)
        {
            throw new ArgumentException("The 'find' value cannot be empty.");
        }

        PlcTextScope textScope = scope switch
        {
            "declaration" => PlcTextScope.Declaration,
            "implementation" => PlcTextScope.Implementation,
            "both" => PlcTextScope.Both,
            _ => throw new ArgumentException("The 'scope' value must be 'declaration', 'implementation', or 'both'."),
        };

        return writer.ReplaceElementText(path, find, replace, textScope, replaceAll);
    }

    private object CreateElement(JsonElement arguments)
    {
        if (automation is not IPlcAutomationWriter writer)
        {
            throw new InvalidOperationException("This CodeTwin instance is read-only.");
        }

        string parentPath = arguments.GetProperty("parentPath").GetString() ?? string.Empty;
        string kind = arguments.GetProperty("kind").GetString() ?? string.Empty;
        if (!ElementKinds.TryGetValue(kind, out PlcElementKind elementKind))
        {
            throw new ArgumentException($"The 'kind' value must be one of: {string.Join(", ", ElementKinds.Keys)}.");
        }

        string name = elementKind switch
        {
            PlcElementKind.PropertyGet => "Get",
            PlcElementKind.PropertySet => "Set",
            _ => GetOptionalString(arguments, "name") ?? string.Empty,
        };
        if (name.Length == 0)
        {
            throw new ArgumentException("The 'name' value cannot be empty.");
        }

        string? returnType = GetOptionalString(arguments, "returnType");
        if (elementKind is PlcElementKind.Function or PlcElementKind.Property && string.IsNullOrWhiteSpace(returnType))
        {
            throw new ArgumentException($"The 'returnType' value is required for kind '{kind}'.");
        }

        string? declaration = GetOptionalString(arguments, "declaration");
        string? implementation = GetOptionalString(arguments, "implementation");
        if (elementKind == PlcElementKind.Folder && (declaration is not null || implementation is not null))
        {
            throw new ArgumentException("A folder has no declaration or implementation.");
        }

        return writer.CreateElement(new PlcCreateRequest(
            parentPath,
            name,
            elementKind,
            returnType,
            declaration,
            implementation));
    }

    private static object ToolError(string message) => new
    {
        content = new[] { new { type = "text", text = message } },
        isError = true,
    };

    private static string? GetOptionalString(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private IPlcAutomationWriter RequireWriter() =>
        automation as IPlcAutomationWriter ?? throw new InvalidOperationException("This CodeTwin instance is read-only.");

    private static JsonElement CreateResponse(object? id, object result) =>
        SerializeToElement(new { jsonrpc = "2.0", id, result });

    private static JsonElement CreateErrorResponse(object? id, int code, string message) =>
        SerializeToElement(new { jsonrpc = "2.0", id, error = new { code, message } });

    private static JsonElement SerializeToElement(object value)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }
}
