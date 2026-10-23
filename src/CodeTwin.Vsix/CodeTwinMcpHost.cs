using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using CodeTwin.Core;

namespace CodeTwin.Vsix;

internal sealed class CodeTwinMcpHost : IDisposable
{
    private readonly InProcessMcpServer server;
    private readonly string discoveryPath;

    private CodeTwinMcpHost(InProcessMcpServer server, string token, string discoveryPath)
    {
        this.server = server;
        Token = token;
        this.discoveryPath = discoveryPath;
    }

    public string Endpoint => server.Endpoint;

    public string Token { get; }

    public static CodeTwinMcpHost Start(IPlcAutomation automation)
    {
        string token = CreateToken();
        int port = ReservePort();
        var server = new InProcessMcpServer(automation, token, port);
        server.Start();

        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodeTwin",
            "instances");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Process.GetCurrentProcess().Id.ToString() + ".json");
        var discovery = new
        {
            pid = Process.GetCurrentProcess().Id,
            endpoint = server.Endpoint,
            token,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(discovery));
        return new CodeTwinMcpHost(server, token, path);
    }

    public void Dispose()
    {
        server.Dispose();
        try
        {
            File.Delete(discoveryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CreateToken()
    {
        byte[] bytes = new byte[32];
        using var random = RandomNumberGenerator.Create();
        random.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static int ReservePort()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }
        finally
        {
            socket.Stop();
        }
    }
}
