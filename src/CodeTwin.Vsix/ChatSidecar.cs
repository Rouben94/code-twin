using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace CodeTwin.Vsix;

/// <summary>Runs the code-twin-chat sidecar and relays JSON lines to and from it.</summary>
internal sealed class ChatSidecar(string executablePath, string mcpUrl, string mcpToken) : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object gate = new();
    private System.Diagnostics.Process? process;
    private StreamWriter? input;
    private bool disposed;

    public event Action<string>? MessageReceived;

    public void Send(string line)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (process is null || process.HasExited)
            {
                Start();
            }

            input!.WriteLine(line);
            input.Flush();
        }
    }

    public void EnsureStarted()
    {
        lock (gate)
        {
            if (!disposed && (process is null || process.HasExited))
            {
                Start();
            }
        }
    }

    public void Dispose()
    {
        System.Diagnostics.Process? running;
        lock (gate)
        {
            disposed = true;
            running = process;
            process = null;
        }

        if (running is null)
        {
            return;
        }

        try
        {
            input?.Dispose();
            if (!running.WaitForExit(3000))
            {
                running.Kill();
            }
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            running.Dispose();
        }
    }

    private void Start()
    {
        process?.Dispose();
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
        };
        startInfo.EnvironmentVariables["CODETWIN_MCP_URL"] = mcpUrl;
        startInfo.EnvironmentVariables["CODETWIN_MCP_TOKEN"] = mcpToken;

        var started = new System.Diagnostics.Process { StartInfo = startInfo };
        started.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                ActivityLog.LogWarning(nameof(ChatSidecar), e.Data);
            }
        };
        started.Start();
        started.BeginErrorReadLine();

        // .NET Framework has no StandardInputEncoding, so wrap the pipe to keep prompts UTF-8.
        input = new StreamWriter(started.StandardInput.BaseStream, Utf8);
        process = started;
        _ = Task.Run(() => ReadOutputAsync(started));
    }

    private async Task ReadOutputAsync(System.Diagnostics.Process source)
    {
        try
        {
            while (await source.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                MessageReceived?.Invoke(line);
            }

            source.WaitForExit();
            if (!disposed)
            {
                string text = string.Format(CultureInfo.InvariantCulture, "The chat sidecar exited with code {0}. Your next message restarts it.", source.ExitCode);
                MessageReceived?.Invoke(JsonSerializer.Serialize(new { type = "error", text }));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            ActivityLog.LogWarning(nameof(ChatSidecar), $"Reading the chat sidecar failed: {exception.Message}");
        }
    }
}
