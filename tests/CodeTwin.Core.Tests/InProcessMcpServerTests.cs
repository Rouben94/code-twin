using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CodeTwin.Core;
using Xunit;

namespace CodeTwin.Core.Tests;

public sealed class InProcessMcpServerTests
{
    [Fact]
    public async Task ReadOnlyToolsWorkOverAuthenticatedHttp()
    {
        const string token = "test-token";
        var elements = new[]
        {
            new PlcElement("Demo", "MAIN", "Demo/TIPC/MAIN", "POU", "VAR\nEND_VAR", "x := 1;"),
            new PlcElement("Demo", "GVL_Global", "Demo/TIPC/GVL_Global", "GVL", "VAR_GLOBAL\nEND_VAR", null),
        };
        int port = ReservePort();
        using var server = new InProcessMcpServer(new DelegatingPlcAutomation(() => elements), token, port);
        server.Start();
        using var client = new HttpClient { BaseAddress = new Uri(server.Endpoint) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        JsonElement tools = await SendAsync(client, 1, "tools/list", null);
        Assert.Equal(3, tools.GetProperty("result").GetProperty("tools").GetArrayLength());

        JsonElement list = await SendAsync(client, 2, "tools/call", new
        {
            name = "plc_list_elements",
            arguments = new { },
        });
        Assert.Contains("MAIN", list.GetProperty("result").GetProperty("structuredContent").GetRawText());

        JsonElement read = await SendAsync(client, 3, "tools/call", new
        {
            name = "plc_read_element",
            arguments = new { path = "Demo/TIPC/MAIN" },
        });
        Assert.Equal("x := 1;", read.GetProperty("result").GetProperty("structuredContent").GetProperty("Implementation").GetString());

        JsonElement search = await SendAsync(client, 4, "tools/call", new
        {
            name = "plc_search_elements",
            arguments = new { query = "global" },
        });
        Assert.Contains("GVL_Global", search.GetProperty("result").GetProperty("structuredContent").GetRawText());
    }

    [Fact]
    public async Task RejectsMissingOrInvalidToken()
    {
        int port = ReservePort();
        using var server = new InProcessMcpServer(new DelegatingPlcAutomation(() => Array.Empty<PlcElement>()), "secret", port);
        server.Start();
        using var client = new HttpClient { BaseAddress = new Uri(server.Endpoint) };
        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Bearer secre")]
    [InlineData("Bearer secret2")]
    [InlineData("bearer secret")]
    public async Task RejectsTokensThatOnlyPartlyMatch(string authorization)
    {
        int port = ReservePort();
        using var server = new InProcessMcpServer(new DelegatingPlcAutomation(() => Array.Empty<PlcElement>()), "secret", port);
        server.Start();
        using var client = new HttpClient { BaseAddress = new Uri(server.Endpoint) };
        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WriteAndSaveToolsUpdateTheAutomationAdapter()
    {
        var automation = new TestAutomation(new PlcElement(
            "Demo",
            "MAIN",
            "Demo/TIPC/MAIN",
            "POU",
            "VAR\nEND_VAR",
            "x := 1;"));
        int port = ReservePort();
        using var server = new InProcessMcpServer(automation, "secret", port);
        server.Start();
        using var client = new HttpClient { BaseAddress = new Uri(server.Endpoint) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");

        JsonElement tools = await SendAsync(client, 1, "tools/list", null);
        Assert.Equal(9, tools.GetProperty("result").GetProperty("tools").GetArrayLength());

        JsonElement write = await SendAsync(client, 2, "tools/call", new
        {
            name = "plc_write_element",
            arguments = new { path = "Demo/TIPC/MAIN", implementation = "x := 2;" },
        });
        Assert.Equal("x := 2;", write.GetProperty("result").GetProperty("structuredContent").GetProperty("Implementation").GetString());

        JsonElement save = await SendAsync(client, 3, "tools/call", new
        {
            name = "plc_save_solution",
            arguments = new { },
        });
        Assert.True(save.GetProperty("result").GetProperty("structuredContent").GetProperty("saved").GetBoolean());
        Assert.True(automation.Saved);

        JsonElement replace = await SendAsync(client, 4, "tools/call", new
        {
            name = "plc_replace_element_text",
            arguments = new
            {
                path = "Demo/TIPC/MAIN",
                find = "x := 2;",
                replace = "x := 3;",
                scope = "implementation",
                replaceAll = false,
            },
        });
        Assert.Equal(1, replace.GetProperty("result").GetProperty("structuredContent").GetProperty("ReplacementCount").GetInt32());
        Assert.Equal("x := 3;", replace.GetProperty("result").GetProperty("structuredContent").GetProperty("Element").GetProperty("Implementation").GetString());
    }

    [Fact]
    public async Task CreateToolCreatesElementsAndReportsToolErrors()
    {
        var automation = new TestAutomation(new PlcElement("Demo", "POUs", "Demo/TIPC/POUs", "Folder", null, null));
        int port = ReservePort();
        using var server = new InProcessMcpServer(automation, "secret", port);
        server.Start();
        using var client = new HttpClient { BaseAddress = new Uri(server.Endpoint) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");

        JsonElement created = await SendAsync(client, 1, "tools/call", new
        {
            name = "plc_create_element",
            arguments = new { parentPath = "Demo/TIPC/POUs", name = "F_Add", kind = "function", returnType = "INT" },
        });
        Assert.Equal("Demo/TIPC/POUs/F_Add", created.GetProperty("result").GetProperty("structuredContent").GetProperty("Path").GetString());
        Assert.Equal(PlcElementKind.Function, automation.LastCreateRequest?.Kind);
        Assert.Equal("INT", automation.LastCreateRequest?.ReturnType);

        JsonElement missingReturnType = await SendAsync(client, 2, "tools/call", new
        {
            name = "plc_create_element",
            arguments = new { parentPath = "Demo/TIPC/POUs", name = "F_Sub", kind = "function" },
        });
        Assert.True(missingReturnType.GetProperty("result").GetProperty("isError").GetBoolean());

        JsonElement missingParent = await SendAsync(client, 3, "tools/call", new
        {
            name = "plc_create_element",
            arguments = new { parentPath = "Demo/TIPC/Missing", name = "FB_Axis", kind = "functionBlock" },
        });
        Assert.True(missingParent.GetProperty("result").GetProperty("isError").GetBoolean());

        JsonElement unknownTool = await SendAsync(client, 4, "tools/call", new { name = "plc_unknown", arguments = new { } });
        Assert.Equal(-32602, unknownTool.GetProperty("error").GetProperty("code").GetInt32());

        JsonElement folder = await SendAsync(client, 5, "tools/call", new
        {
            name = "plc_create_element",
            arguments = new { parentPath = "Demo/TIPC/POUs", name = "Test", kind = "folder" },
        });
        Assert.Equal("Demo/TIPC/POUs/Test", folder.GetProperty("result").GetProperty("structuredContent").GetProperty("Path").GetString());
        Assert.Equal(PlcElementKind.Folder, automation.LastCreateRequest?.Kind);

        JsonElement folderWithCode = await SendAsync(client, 6, "tools/call", new
        {
            name = "plc_create_element",
            arguments = new { parentPath = "Demo/TIPC/POUs", name = "Test2", kind = "folder", implementation = "x := 1;" },
        });
        Assert.True(folderWithCode.GetProperty("result").GetProperty("isError").GetBoolean());

        automation.CreateFailure = new NotImplementedException("Not supported.");
        JsonElement unexpected = await SendAsync(client, 7, "tools/call", new
        {
            name = "plc_create_element",
            arguments = new { parentPath = "Demo/TIPC/POUs", name = "Run", kind = "method" },
        });
        Assert.True(unexpected.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("Not supported.", unexpected.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);

        automation.CreateFailure = null;
        JsonElement getter = await SendAsync(client, 8, "tools/call", new
        {
            name = "plc_create_element",
            arguments = new { parentPath = "Demo/TIPC/POUs", kind = "propertyGet", implementation = "Value := _value;" },
        });
        Assert.Equal("Demo/TIPC/POUs/Get", getter.GetProperty("result").GetProperty("structuredContent").GetProperty("Path").GetString());
        Assert.Equal(PlcElementKind.PropertyGet, automation.LastCreateRequest?.Kind);

        JsonElement setter = await SendAsync(client, 9, "tools/call", new
        {
            name = "plc_create_element",
            arguments = new { parentPath = "Demo/TIPC/POUs", name = "Other", kind = "propertySet" },
        });
        Assert.Equal("Set", automation.LastCreateRequest?.Name);
        Assert.False(setter.GetProperty("result").TryGetProperty("isError", out _));

        JsonElement missingName = await SendAsync(client, 10, "tools/call", new
        {
            name = "plc_create_element",
            arguments = new { parentPath = "Demo/TIPC/POUs", kind = "method" },
        });
        Assert.True(missingName.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task BuildToolsReturnDiagnostics()
    {
        var automation = new TestAutomation(new PlcElement("Demo", "MAIN", "Demo/TIPC/MAIN", "POU", null, null));
        int port = ReservePort();
        using var server = new InProcessMcpServer(automation, "secret", port);
        server.Start();
        using var client = new HttpClient { BaseAddress = new Uri(server.Endpoint) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");

        JsonElement build = await SendAsync(client, 1, "tools/call", new { name = "plc_build", arguments = new { } });
        JsonElement buildResult = build.GetProperty("result").GetProperty("structuredContent");
        Assert.False(buildResult.GetProperty("Succeeded").GetBoolean());
        Assert.Equal(3, buildResult.GetProperty("Diagnostics")[0].GetProperty("Line").GetInt32());
        Assert.Equal("C0077", buildResult.GetProperty("Diagnostics")[0].GetProperty("Code").GetString());

        JsonElement errors = await SendAsync(client, 2, "tools/call", new { name = "plc_get_errors", arguments = new { } });
        Assert.Equal("error", errors.GetProperty("result").GetProperty("structuredContent")[0].GetProperty("Severity").GetString());
    }

    [Fact]
    public async Task FollowsStreamableHttpTransportRules()
    {
        int port = ReservePort();
        using var server = new InProcessMcpServer(new DelegatingPlcAutomation(() => Array.Empty<PlcElement>()), "secret", port);
        server.Start();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");
        string endpointWithoutSlash = server.Endpoint.TrimEnd('/');

        using var notification = new StringContent("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", Encoding.UTF8, "application/json");
        using HttpResponseMessage notificationResponse = await client.PostAsync(endpointWithoutSlash, notification);
        Assert.Equal(HttpStatusCode.Accepted, notificationResponse.StatusCode);

        using HttpResponseMessage getResponse = await client.GetAsync(server.Endpoint);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getResponse.StatusCode);

        using var foreignOrigin = new HttpRequestMessage(HttpMethod.Post, server.Endpoint)
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", Encoding.UTF8, "application/json"),
        };
        foreignOrigin.Headers.Add("Origin", "https://evil.example");
        using HttpResponseMessage foreignResponse = await client.SendAsync(foreignOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, int id, string method, object? parameters)
    {
        var message = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters,
        };
        using var content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(string.Empty, content);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class TestAutomation(PlcElement initial) : IPlcAutomationWriter
    {
        private PlcElement element = initial;

        public bool Saved { get; private set; }

        public IReadOnlyList<PlcElement> ListElements() => new[] { element };

        public PlcElement? ReadElement(string path) =>
            string.Equals(path, element.Path, StringComparison.OrdinalIgnoreCase) ? element : null;

        public IReadOnlyList<PlcElement> SearchElements(string query) =>
            new DelegatingPlcAutomation(ListElements).SearchElements(query);

        public PlcElement WriteElement(string path, string? declaration, string? implementation)
        {
            if (ReadElement(path) is null)
            {
                throw new KeyNotFoundException(path);
            }

            element = new PlcElement(
                element.ProjectName,
                element.Name,
                element.Path,
                element.ItemType,
                declaration ?? element.Declaration,
                implementation ?? element.Implementation);
            return element;
        }

        public PlcReplaceResult ReplaceElementText(string path, string find, string replace, PlcTextScope scope, bool replaceAll)
        {
            if (ReadElement(path) is null)
            {
                throw new KeyNotFoundException(path);
            }

            string? declaration = element.Declaration;
            string? implementation = element.Implementation;
            int count = 0;
            if (scope is PlcTextScope.Declaration or PlcTextScope.Both)
            {
                declaration = Replace(declaration, find, replace, replaceAll, ref count);
            }

            if (scope is PlcTextScope.Implementation or PlcTextScope.Both)
            {
                implementation = Replace(implementation, find, replace, replaceAll, ref count);
            }

            if (count == 0)
            {
                throw new InvalidOperationException("Not found.");
            }

            element = new PlcElement(element.ProjectName, element.Name, element.Path, element.ItemType, declaration, implementation);
            return new PlcReplaceResult(element, count);
        }

        public void Save() => Saved = true;

        public PlcElement CreateElement(PlcCreateRequest request)
        {
            if (ReadElement(request.ParentPath) is null)
            {
                throw new KeyNotFoundException($"PLC element '{request.ParentPath}' was not found.");
            }

            if (CreateFailure is not null)
            {
                throw CreateFailure;
            }

            LastCreateRequest = request;
            return new PlcElement(element.ProjectName, request.Name, request.ParentPath + "/" + request.Name, request.Kind.ToString(), request.Declaration, request.Implementation);
        }

        public Exception? CreateFailure { get; set; }

        public PlcCreateRequest? LastCreateRequest { get; private set; }

        public PlcBuildResult Build() => new(false, GetDiagnostics());

        public IReadOnlyList<PlcDiagnostic> GetDiagnostics() =>
            new[] { new PlcDiagnostic("error", "Unknown type: 'FOO'", "MAIN.TcPOU", 3, 5, "Demo", "C0077") };

        private static string? Replace(string? source, string find, string replace, bool replaceAll, ref int count)
        {
            if (source is null)
            {
                return null;
            }

            if (replaceAll)
            {
                int offset = 0;
                while ((offset = source.IndexOf(find, offset, StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    offset += find.Length;
                }

                return source.Replace(find, replace, StringComparison.Ordinal);
            }

            int index = source.IndexOf(find, StringComparison.Ordinal);
            if (index < 0)
            {
                return source;
            }

            count++;
            return source.Substring(0, index) + replace + source.Substring(index + find.Length);
        }
    }
}
