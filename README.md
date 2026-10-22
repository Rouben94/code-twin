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

Copilot CLI: add an `http` server with the endpoint and an `Authorization` header from the discovery file to `~/.copilot/mcp-config.json`.

Licensed under [MPL-2.0](LICENSE).
