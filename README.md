# CodeTwin

AI engineering for TwinCAT: a Visual Studio / TwinCAT XAE Shell extension that lets AI agents read, edit and build TwinCAT projects through the Automation Interface.

Build with `dotnet build CodeTwin.slnx -c Release`. Install into TwinCAT XAE Shell with `tools\Install-TcXaeShell.ps1`; details and Visual Studio install in [docs/spike.md](docs/spike.md).

## MCP server

When the extension loads, it starts an MCP server (Streamable HTTP, JSON responses) on `http://127.0.0.1:<port>/mcp` inside the shell process. Every request needs `Authorization: Bearer <token>`. Port and token are written to `%LOCALAPPDATA%\CodeTwin\instances\<pid>.json` and deleted when the shell closes.

Tools: `plc_list_elements`, `plc_read_element`, `plc_search_elements`, `plc_write_element`, `plc_replace_element_text`, `plc_create_element`, `plc_save_solution`, `plc_build`, `plc_get_errors`. Element paths look like `<TwinCAT project>/<PLC>/<PLC project>/POUs/MAIN`; get them from `plc_list_elements`.

Manual test from PowerShell with a TwinCAT solution open:

```powershell
$d = Get-ChildItem "$env:LOCALAPPDATA\CodeTwin\instances\*.json" | Sort-Object LastWriteTime | Select-Object -Last 1 | Get-Content | ConvertFrom-Json
function Invoke-Mcp($method, $params) {
    $body = @{ jsonrpc = '2.0'; id = 1; method = $method; params = $params } | ConvertTo-Json -Depth 10
    Invoke-RestMethod $d.endpoint -Method Post -Body $body -ContentType 'application/json' -Headers @{ Authorization = "Bearer $($d.token)" }
}
(Invoke-Mcp 'tools/list' @{}).result.tools.name
(Invoke-Mcp 'tools/call' @{ name = 'plc_build'; arguments = @{} }).result.structuredContent
```

Copilot CLI and other stdio MCP clients connect through the `code-twin-mcp` bridge (see [TwinCAT agent plugin](#twincat-agent-plugin)); no manual registration.

## TwinCAT agent plugin

`plugins/twincat` is a Copilot CLI plugin with a `twincat` agent and skills for TwinCAT 3 PLC work: InfoSys search, naming, ST syntax, OOP and patterns, data types and memory, compiler errors, TcUnit and motion. The agent looks things up on Beckhoff InfoSys first (live search), then the web, and edits PLC code only through the CodeTwin MCP server. It never performs online actions (activate, login, download).

Install from this repository's marketplace (or use a local clone path instead of `Rouben94/code-twin`):

```powershell
copilot plugin marketplace add Rouben94/code-twin
copilot plugin install twincat@code-twin
```

That is the only setup step. The plugin starts its MCP server `codetwin` itself: `plugins/twincat/bin/code-twin-mcp.exe`, a stdio bridge that forwards the tool calls to the CodeTwin server in a running TwinCAT XAE Shell. It needs the .NET 10 runtime and the CodeTwin extension in TwinCAT XAE Shell.

The bridge adds one tool, `initialize`, which the agent calls first with the absolute path of the repository, folder, `.sln`, `.tsproj` or `.plcproj` it works on. It finds the TwinCAT solution, connects to the running shell that has it open (discovery files in `%LOCALAPPDATA%\CodeTwin\instances`), or starts TcXaeShell with it. A shell started this way is launched through Explorer, so it stays open when Copilot exits. Nothing about the repository is stored; the binding lives only as long as the Copilot session. When the shell restarts, the next tool call finds it again. If the path has no TwinCAT solution (Copilot started in an unrelated folder), `initialize` connects to the only shell that has a solution open. The `plc_*` tools fail until `initialize` succeeded, so a session never edits a project it was not pointed at.

If you registered the server manually before, remove that registration once, since it clashes with the plugin's `codetwin` server:

```powershell
copilot mcp remove codetwin
```

After changing `src/CodeTwin.Mcp`, refresh the shipped exe with `dotnet publish src\CodeTwin.Mcp -c Release` (writes to `plugins/twincat/bin`). Running Copilot sessions lock the exe; rename it to `code-twin-mcp.exe.old` first, new sessions then use the new exe.

Use it with `copilot --agent twincat:twincat` or pick `twincat` in `/agent`. The fully qualified name avoids a clash with a user-level agent also called `twincat` (user agents win over plugin agents). Example prompts:

- `Implement F_Add with two INT inputs and call it from MAIN, then build and fix all errors.`
- `Write TcUnit tests for FB_Valve.`
- `What does compiler error C0077 mean?`

The InfoSys search runs a PowerShell script from the plugin; approve it when asked. Update with `copilot plugin update twincat@code-twin`, remove with `copilot plugin uninstall twincat@code-twin`.

Licensed under [MPL-2.0](LICENSE).
