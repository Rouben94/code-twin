using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace CodeTwin.Core;

public sealed class InProcessMcpServer : IDisposable
{
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

            if (!IsAuthorized(context.Request.Headers["Authorization"]))
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

    // Constant-time comparison, so response timing does not reveal how much of the token matched.
    private bool IsAuthorized(string? authorization)
    {
        byte[] expected = Encoding.UTF8.GetBytes("Bearer " + token);
        byte[] actual = Encoding.UTF8.GetBytes(authorization ?? string.Empty);
        int difference = expected.Length ^ actual.Length;
        for (int i = 0; i < expected.Length; i++)
        {
            difference |= expected[i] ^ (i < actual.Length ? actual[i] : 0);
        }

        return difference == 0;
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
                    tools = McpToolCatalog.GetTools(automation is IPlcAutomationWriter),
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
            "plc_search_elements" => () => automation.SearchElements(GetRequiredString(arguments, "query")),
            "plc_write_element" => () => WriteElement(arguments),
            "plc_save_solution" => SaveSolution,
            "plc_replace_element_text" => () => ReplaceElementText(arguments),
            "plc_create_element" => () => CreateElement(arguments),
            "plc_rename_element" => () => RenameElement(arguments),
            "plc_delete_element" => () => DeleteElement(arguments),
            "plc_read_visualization" => () => ReadVisualization(arguments),
            "plc_write_visualization" => () => WriteVisualization(arguments),
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

        JsonElement structured = JsonSerializer.SerializeToElement(content);
        return new
        {
            content = new[] { new { type = "text", text = structured.GetRawText() } },
            // MCP requires structuredContent to be an object; clients that validate it (Claude Code) reject a bare array.
            structuredContent = structured.ValueKind == JsonValueKind.Object ? structured : (object)new { items = structured },
        };
    }

    private object ReadElement(JsonElement arguments)
    {
        string path = GetRequiredString(arguments, "path");
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

        string path = GetRequiredString(arguments, "path");
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

        string path = GetRequiredString(arguments, "path");
        string find = GetRequiredString(arguments, "find");
        string replace = GetRequiredString(arguments, "replace");
        string scope = GetRequiredString(arguments, "scope");
        bool replaceAll = GetRequiredBoolean(arguments, "replaceAll");
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

        string parentPath = GetRequiredString(arguments, "parentPath");
        string kind = GetRequiredString(arguments, "kind");
        if (!McpToolCatalog.ElementKinds.TryGetValue(kind, out PlcElementKind elementKind))
        {
            throw new ArgumentException($"The 'kind' value must be one of: {string.Join(", ", McpToolCatalog.ElementKinds.Keys)}.");
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
        if (elementKind is PlcElementKind.Folder or PlcElementKind.Visualization && (declaration is not null || implementation is not null))
        {
            throw new ArgumentException($"A {kind} has no declaration or implementation.");
        }

        if (elementKind == PlcElementKind.Alias && string.IsNullOrWhiteSpace(declaration))
        {
            throw new ArgumentException("The 'declaration' value is required for kind 'alias'.");
        }

        return writer.CreateElement(new PlcCreateRequest(
            parentPath,
            name,
            elementKind,
            returnType,
            declaration,
            implementation));
    }

    private object RenameElement(JsonElement arguments)
    {
        string path = GetRequiredString(arguments, "path");
        string newName = GetRequiredString(arguments, "newName");
        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOf('/') >= 0)
        {
            throw new ArgumentException("The 'newName' value must be a non-empty name without '/'.");
        }

        return RequireWriter().RenameElement(path, newName);
    }

    private object DeleteElement(JsonElement arguments)
    {
        string path = GetRequiredString(arguments, "path");
        RequireWriter().DeleteElement(path);
        return new { deleted = true, path };
    }

    private object ReadVisualization(JsonElement arguments)
    {
        string path = GetRequiredString(arguments, "path");
        return new { path, xml = RequireWriter().ReadVisualization(path) };
    }

    private object WriteVisualization(JsonElement arguments)
    {
        string path = GetRequiredString(arguments, "path");
        string xml = GetRequiredString(arguments, "xml");
        string name = path.Substring(path.LastIndexOf('/') + 1);
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException exception)
        {
            throw new ArgumentException($"The 'xml' value is not valid XML: {exception.Message}");
        }

        string? visualizationName = document.Root?.Name.LocalName == "TcPlcObject"
            ? (string?)document.Root.Element("Visu")?.Attribute("Name")
            : null;
        if (!string.Equals(visualizationName, name, StringComparison.Ordinal))
        {
            throw new ArgumentException($"The 'xml' value must be a TcVIS document with <TcPlcObject><Visu Name=\"{name}\">. Rename with plc_rename_element.");
        }

        RequireWriter().WriteVisualization(path, xml);
        return new { written = true, path };
    }

    private static object ToolError(string message) => new
    {
        content = new[] { new { type = "text", text = message } },
        isError = true,
    };

    // Agents sometimes call a tool before loading its schema; naming the argument lets them correct the call.
    private static JsonElement GetRequired(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null
            ? value
            : throw new ArgumentException($"Missing required argument '{name}'.");

    private static string GetRequiredString(JsonElement arguments, string name) =>
        GetRequired(arguments, name) is { ValueKind: JsonValueKind.String } value
            ? value.GetString() ?? string.Empty
            : throw new ArgumentException($"The '{name}' argument must be a string.");

    private static bool GetRequiredBoolean(JsonElement arguments, string name) =>
        GetRequired(arguments, name).ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentException($"The '{name}' argument must be true or false."),
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
