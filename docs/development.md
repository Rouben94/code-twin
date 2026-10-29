# Development

Details behind the [README](../README.md): how the parts work, and how to build and release.

## Setup

The setup installs per user (no admin rights) into `%LOCALAPPDATA%\Programs\CodeTwin`:

- the extension into TcXaeShell (through `tools\Install-TcXaeShell.ps1`),
- the [TwinCAT agent plugin](#twincat-agent-plugin) with the `code-twin-mcp` bridge, registered with Copilot CLI and the Copilot app as a local marketplace (`copilot plugin marketplace add`, `copilot plugin install twincat@code-twin`).

Uninstalling removes the extension and the Copilot registration as well.

## Build and CI

Build the setup with `installer\Build-Setup.ps1` (needs the .NET 10 SDK, the Visual Studio SDK build tools and [Inno Setup 6](https://jrsoftware.org/isinfo.php)); it writes `artifacts\CodeTwin-Setup-<version>.exe`. For development, `dotnet build CodeTwin.slnx -c Release` and `tools\Install-TcXaeShell.ps1` deploy just the extension; details and Visual Studio install in [spike.md](spike.md).

CI (`.github/workflows/ci.yml`) builds, tests and builds the setup for every pull request and push to `main`; the setup is kept as a workflow artifact for 14 days. Every push to `main` also publishes a GitHub release `v<MAJOR>.<MINOR>.<PATCH>`: MAJOR.MINOR come from `Directory.Build.props`, PATCH counts up automatically from the last release of that MAJOR.MINOR and restarts at 0 when you change it.

## MCP server

When the extension loads, it starts an MCP server (Streamable HTTP, JSON responses) on `http://127.0.0.1:<port>/mcp` inside the shell process. Every request needs `Authorization: Bearer <token>`. Port and token are written to `%LOCALAPPDATA%\CodeTwin\instances\<pid>.json` and deleted when the shell closes.

Tools: `plc_list_elements`, `plc_read_element`, `plc_search_elements`, `plc_write_element`, `plc_replace_element_text`, `plc_create_element`, `plc_rename_element`, `plc_delete_element`, `plc_save_solution`, `plc_build`, `plc_get_errors`. Element paths look like `<TwinCAT project>/<PLC>/<PLC project>/POUs/MAIN`; get them from `plc_list_elements`.

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

## Chat window

**Tools > CodeTwin > Chat** (command `Tools.CodeTwin.Chat`) opens a dockable chat with the `twincat` agent inside the shell. It works on the solution open in this shell. Each prompt carries the solution and the active POU (the editor that has focus), so there is no `initialize` step. The header shows that context.

Requirements:

- Copilot CLI installed (`copilot` on `PATH`, or set `COPILOT_CLI_PATH` to `copilot.exe`). The chat uses your Copilot login and subscription. If you are not signed in, the chat shows a **Sign in** button that opens Copilot CLI's own sign-in (`copilot login`) in a separate window. CodeTwin never reads, stores or forwards credentials.
- .NET 10 runtime.
- WebView2 runtime (part of Windows 11 and current Windows 10).

The window hosts a WebView2 page (`src/CodeTwin.Vsix/ChatUi`) and starts the sidecar `chat\code-twin-chat.exe` (`src/CodeTwin.Chat`). The sidecar drives the Copilot CLI through the GitHub Copilot SDK. The VSIX ships its own copy of the `twincat` agent and skills, so the plugin is not needed. The sidecar connects Copilot straight to this shell's MCP server, with endpoint and token passed in by the extension.

CodeTwin tool calls and reads inside the solution folder or the shipped agent and skills run without asking. Anything else, such as reads elsewhere, the PowerShell script of the InfoSys search, shell commands, file writes or URLs, shows an Allow/Deny card in the chat. Reads from network paths are never approved automatically. **New chat** starts a fresh session. The sidecar starts when the window opens and stops with the shell.

Chats are saved by the Copilot CLI in its own session store (`%USERPROFILE%\.copilot\session-state`). The **history** button lists the CodeTwin chats of the open solution; pick one to reload it and continue, or delete it. A new chat shows the five most recent ones on its start page. The **lock** button starts a private chat; click it again to leave it and start a saved chat. A private chat is not listed and is deleted when you leave it, start another chat, switch chats or close the shell. Prompts of a private chat still go to GitHub Copilot.

The **paperclip** button attaches files to the next message; you can also drop files on the chat or paste them (pasted screenshots are saved under `%TEMP%\CodeTwin\Attachments`). The agent gets each file's path and may read it without asking. Copilot sends images and documents natively. Claude gets images (up to 3.75 MB) and PDFs (up to 20 MB) in the message and opens any other file with its Read tool.

The bar under the message box picks the mode and the model, and shows usage:

- **Mode**: Agent (default), Plan (the agent proposes a plan and waits for your approval before it edits) or Autopilot. Shift+Tab cycles the modes.
- **Tool permissions** (at the bottom of the mode menu): Always ask (default) asks before shell commands, file edits, web access and reads outside the project, while project reads and the CodeTwin tools run directly. Approve all runs every tool without asking; it is not remembered, so every new or reopened chat starts with Always ask.
- **Model**: every model your Copilot subscription offers, with its premium request multiplier and, where the model supports it, the reasoning effort. Mode, model and effort are remembered.
- **Usage ring**: how full the context window is. Click it for this chat's token totals and AI credits, and your premium request quota with its reset date.

The mascot Tick shows the agent state with its eye LEDs: green (RUN, ready), blinking amber (WARNING, thinking or waiting for you), red (ERROR, request failed). It is drawn from a 17 x 12 pixel grid in `ChatUi/tick.js`; there are no image files. A second Tick lives behind the message box (`ChatUi/tick-walker.js`): now and then it peeks over the edge, strolls along the top, naps, and kneads while you type. It stays off when Windows animations are disabled. In a private chat, both Ticks wear sunglasses; a small LED reflection in the lenses keeps the state colour visible.

## TwinCAT agent plugin

`plugins/twincat` is a Copilot CLI plugin with a `twincat` agent and skills for TwinCAT 3 PLC work: InfoSys search, naming, ST syntax, OOP and patterns, data types and memory, compiler errors, TcUnit and motion. The agent looks things up on Beckhoff InfoSys first (live search), then the web, and edits PLC code only through the CodeTwin MCP server. It never performs online actions (activate, login, download).

The setup installs and registers the plugin (see [Setup](#setup)); there is no other setup step. The plugin starts its MCP server `codetwin` itself: `bin\code-twin-mcp.exe` in the installed plugin, a stdio bridge that forwards the tool calls to the CodeTwin server in a running TwinCAT XAE Shell.

The bridge adds one tool, `initialize`, which the agent calls first with the absolute path of the repository, folder, `.sln`, `.tsproj` or `.plcproj` it works on. It finds the TwinCAT solution, connects to the running shell that has it open (discovery files in `%LOCALAPPDATA%\CodeTwin\instances`), or starts TcXaeShell with it. A shell started this way is launched through Explorer, so it stays open when Copilot exits. Nothing about the repository is stored; the binding lives only as long as the Copilot session. When the shell restarts, the next tool call finds it again. If the path has no TwinCAT solution (Copilot started in an unrelated folder), `initialize` connects to the only shell that has a solution open and says so in its result. The `plc_*` tools fail until `initialize` succeeded, so a session never edits a project it was not pointed at. Network and device paths (`\\server\share`, `\\?\`) are refused; open the solution from a local drive.

### Multi-repo sessions

A session can span a PLC repository and related repositories, for example an HMI or docs. In the Copilot app, add a folder that contains the repositories; in Copilot CLI, start in one and add the others with `--add-dir`. The agent calls `initialize` with the working directory as `path` and the other repositories in `additionalPaths`. `initialize` uses the one TwinCAT solution found across all paths; with several, it asks for the `.sln`. A worktree is a different path from the main checkout, so a session in a worktree gets its own shell with the worktree's solution.

The agent then edits the PLC project only through the CodeTwin tools and the other repositories with the normal file tools, and builds the PLC project before it reports. Repository-specific rules belong in that repository, for example `.github/copilot-instructions.md` or `AGENTS.md`; the agent reads them before changing the repository. The plugin contains no repository data.

Known limitation: the Copilot app currently hides the plugin's `codetwin` tools from sessions that select the `twincat:twincat` agent. In the app, start the session without an agent and tell it to follow the `twincat` agent; Copilot CLI with `--agent twincat:twincat` is not affected.
If you registered the server manually before, remove that registration once, since it clashes with the plugin's `codetwin` server:

```powershell
copilot mcp remove codetwin
```

The plugin is loaded live from the install folder, so a new setup takes effect with the next Copilot session. Running Copilot sessions lock `code-twin-mcp.exe`; the setup asks you to close them.

Use it with `copilot --agent twincat:twincat` or pick `twincat` in `/agent`. The fully qualified name avoids a clash with a user-level agent also called `twincat` (user agents win over plugin agents). Example prompts:

- `Implement F_Add with two INT inputs and call it from MAIN, then build and fix all errors.`
- `Write TcUnit tests for FB_Valve.`
- `What does compiler error C0077 mean?`

The InfoSys search runs a PowerShell script from the plugin; approve it when asked.
