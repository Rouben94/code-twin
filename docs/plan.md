# CodeTwin – Plan

## Goal
A VSIX for the TwinCAT XAE Shell (4026, x64) that hosts our own MCP server in-process,
our own TwinCAT agent + skills, an in-shell chat window, and a localhost MCP endpoint
usable from Copilot CLI, Copilot Desktop and Claude Desktop (multi-repo work).

## License / compliance rules (apply to every phase)
- Third-party TwinCAT AI tools: used only as a reference for *behaviour*. No copying of their code,
  prompts or skills, no decompiling or bundling their binaries. Everything here is written from scratch.
- Beckhoff: use only the public, documented Automation Interface (EnvDTE, ITcSysManager,
  ITcPlcDeclaration/Implementation, ITcPlcIECProject …). No Beckhoff DLLs redistributed –
  reference the installed interop assemblies. Each user needs their own TwinCAT XAE install.
- InfoSys content is Beckhoff copyright: query it live at runtime and quote/link it;
  never crawl, cache in bulk or ship an index of it.

## Tech stack
- Language: C# everywhere.
- VSIX: .NET Framework 4.8 (required by the VS 2022-based TcXaeShell).
- Bridge (`code-twin-mcp`), tools, tests: .NET 10 LTS (`net10.0-windows`), official MCP C# SDK.
- Shared core (TcAutomation, tool definitions): multi-target `net48;net10.0-windows` (or netstandard2.0).
- Chat UI: WebView2 in a native VS tool window, plain HTML/JS front end (no npm toolchain in
  `dotnet build`); VS theme colors passed to CSS variables and updated on theme change. Agent/skills: Markdown.
- Chat sidecar (`code-twin-chat`): .NET 10, GitHub Copilot SDK driving the installed Copilot CLI.

## Architecture
```
TcXaeShell (VS 2022 shell)
 └─ CodeTwin.vsix  (.NET Framework 4.8, AsyncPackage, autoload on SolutionExists)
     ├─ TcAutomation core   – DTE/ITcSysManager access, UI-thread marshalling, retry filter
     ├─ MCP server          – Streamable HTTP on 127.0.0.1:<port>/mcp, token auth
     │    tools: list/read/grep/write/replace POU, create POU/DUT/GVL/method/property,
     │           build, errors, save; edits keep the editor open
     ├─ Discovery file      – %LOCALAPPDATA%\CodeTwin\instances\<pid>.json (port + token only)
     └─ Chat tool window    – WebView2 UI ↔ code-twin-chat (Copilot SDK, out-of-proc)
                               ↔ copilot CLI → in-shell MCP server over HTTP
Clients: in-shell chat (direct HTTP), Copilot CLI, Copilot Desktop, Claude Desktop (via stdio→HTTP bridge)
```

## Phases
0. **Spike** – minimal VSIX installs into TcXaeShell x64 4026, autoloads, shows a tool window,
   reads the open PLC project tree. Confirms install path/manifest target. (go/no-go)
1. **Core + MCP server** – in-process HTTP MCP server with read tools, then write tools
   (whole-text + find/replace edits, editor kept open), build + error list, save.
   Test with Copilot CLI against a sample project.
2. **Agent + skills** – own `twincat` agent: InfoSys first (live query), web second;
   own skills: naming, ST syntax, OOP/patterns, data types/memory, compiler errors,
   TcUnit, motion. Packaged as a Copilot CLI plugin (local marketplace installed by the setup).
3. **Chat window** – dockable WebView2 chat in the shell, backed by Copilot SDK/CLI sidecar
   with our MCP server + agent preloaded; context = current solution / active POU.
   Done:
   - The sidecar runs out of process because the SDK needs newer runtime assemblies than VS 17 binds.
     It uses the installed, signed-in Copilot CLI instead of the SDK's bundled CLI (~250 MB).
   - The sidecar connects the session directly to the in-shell HTTP MCP server, not to the bridge.
     The extension already knows endpoint and token, so there is no discovery, no `initialize`
     and no doubt about which shell is meant. That also saves a process. The bridge stays for
     external clients.
   - The agent and skills ship inside the VSIX (`chat\agents`, `chat\skills`).
   - Each prompt carries a `<context>` block with the solution and the active POU.
   - CodeTwin tools and reads inside the solution folder (and the shipped agent and skills) are
     auto-approved; other permission requests, including reads elsewhere and every network path,
     are asked in the UI. This uses the SDK's experimental permission API (GHCP001). The mode menu
     has a Tool permissions submenu (Always ask, Approve all); Approve all approves every request
     and is reset to Always ask with every new or resumed chat.
   - The composer has a mode picker (Agent, Plan, Autopilot), a model picker with reasoning effort,
     and a usage ring (context window, token totals, premium requests). Modes use the experimental
     `session.mode.set` RPC, because `MessageOptions.AgentMode` alone does not switch the session.
     Quota uses the experimental account quota RPC. Both are GHCP001.
   - The usage popover also shows the AI credits of the current chat (experimental usage metrics RPC).
   - CodeTwin never handles LLM credentials. The sidecar only asks Copilot CLI whether it is signed in
     (`GetAuthStatusAsync`); if not, the chat offers a Sign in button that starts `copilot login` in
     its own window. An earlier monthly AI-credits figure read a GitHub token (environment variables,
     `gh auth token`) for an undocumented endpoint; it was removed in Phase 6.
   - Chat history mirrors the Copilot CLI session store instead of keeping a separate one. CodeTwin
     sessions get a `codetwin-` ID; the list shows those whose working directory is the solution
     folder (the SDK's list filter is ignored, so the sidecar filters). Resuming replays the stored
     user, assistant and tool events. Private chats use `codetwin-private-` and are deleted on close;
     `EnableSessionStore = false` still stores the session, so it is not used. Leftover private
     sessions, e.g. after a crash, are deleted the next time the history is listed.
4. **Generic desktop package** – one global Copilot/Claude plugin = agent + skills + MCP config
   pointing to a generic `code-twin-mcp` bridge. Contains NO repository-specific data.
   The project is resolved at call time from the path the desktop app passes
   (`initialize(<repo or .plcproj path>)`): find the .sln/.tsproj in that repo → ask running
   shells (via discovery files) which one has it open → else launch TcXaeShell with it.
   Nothing about the repo is stored or shared between sessions.
   Done before Phase 3: the `twincat` plugin ships `code-twin-mcp` and its `mcp.json`, so
   installing the plugin is the only Copilot CLI setup step. Phase 3 uses the in-shell HTTP
   server directly instead of the bridge (see Phase 3).
5. **Multi-repo use** – in the desktop app add any repo containing a PLC project plus related
   repos; the same agent edits, builds and checks errors in the PLC project through the bridge
   and edits the other repos with normal file tools. Optional per-repo instructions stay in
   that repo (e.g. `.github/copilot-instructions.md`), never in the package.
   Done:
   - `initialize` took a single path. In a multi-repo session the working directory can be a
     non-PLC repo; then it found no solution and fell back to the only open shell, which may hold
     an unrelated project, without saying so.
   - `initialize` now also takes `additionalPaths` (the session's other repos) and uses the one
     TwinCAT solution found across all paths, de-duplicated; several solutions are an error that
     asks for the `.sln`. The fallback to the only open shell stays, but its result
     (`foundInPaths: false`) tells the agent to check that it is the right project.
   - The `twincat` agent passes all repos of the session to `initialize`, edits the other repos with
     file tools after reading their own instructions (`.github/copilot-instructions.md`, `AGENTS.md`),
     and builds the PLC project before it reports. The package still holds no repo data.
   - The chat window, the in-shell HTTP server and the `plc_*` tools are unchanged; the chat has no
     `initialize` step.
   - Verified in the Copilot app with a folder project holding a PLC repo and an HMI repo: one
     session following the `twincat` agent rules started a shell through `initialize`
     (`foundInPaths: true`), changed both repos, and the PLC project built without errors.
   - Open: when a session selects the plugin agent `twincat:twincat` in the Copilot app, the app
     does not offer the plugin's `codetwin` tools (`tools: ["*"]` in the agent does not help).
     Copilot CLI with `--agent twincat:twincat` and app sessions without an agent have them.
6. **Hardening** – permission modes (read / edit / build / online), blocked AMS Net IDs,
   no activate/login/start without confirmation, signed VSIX, tests.
   - Installer (one setup, per user, with uninstall): installs the VSIX into TcXaeShell and
     `code-twin-mcp` into a fixed folder, then registers the bridge with the clients it finds,
     through each client's own CLI where one exists (no hand-edited foreign config files).
   - Client packages, all using the same bridge and skills (`SKILL.md`):
     - Copilot CLI / Copilot app: `twincat` plugin (agent + skills + `mcp.json`), as today
     - Claude Code: plugin with the `twincat` agent, skills and MCP config
     - Codex CLI, VS Code / Cursor: MCP registration; agent and skills where the client supports them
     - Claude Desktop: MCP registration (config entry or `.mcpb` bundle)
     - ChatGPT desktop: verify whether local (stdio) MCP servers are supported; otherwise out of scope
   - The TwinCAT rules live only in the `twincat` agent and apply only when the user picks it.
     The MCP server's instructions stay limited to tool usage (call `initialize` first).
   - Security review before shipping, done:
     - Chat: reads are auto-approved only inside the solution folder and the shipped agent and skills;
       network paths are never auto-approved (they would send the Windows credentials).
       Approve all is no longer persisted.
     - Bridge: `initialize` refuses network and device paths; discovery endpoints must be
       `http` loopback `/mcp`, so the token is never sent elsewhere.
     - In-shell server: constant-time token comparison.
     - No LLM credential handling: the monthly AI-credits reader (GitHub token, `gh auth token`)
       and the account auth RPC are removed; sign-in is delegated to `copilot login`.
   - Installer, done (`installer\`): Inno Setup, per user, into `%LOCALAPPDATA%\Programs\CodeTwin`.
     It calls `tools\Install-TcXaeShell.ps1` for the extension (and `-Uninstall` on uninstall),
     checks for the .NET 10 runtime and a closed TcXaeShell, and registers Copilot CLI and app by
     adding the install folder as the local marketplace `code-twin` (the plugin is loaded live from
     there). `code-twin-mcp` sits in the plugin's `bin` folder, so `mcp.json` stays relative and no
     `PATH` change is needed; later clients (Claude Code, Codex) get the absolute path.
     `installer\Build-Setup.ps1` builds the VSIX, publishes the bridge and compiles the setup into
     `artifacts\`. No build artifacts in git.
   - CI, done (`.github/workflows/ci.yml`): build, test and setup on every pull request and push
     to `main`; every push to `main` releases `v<MAJOR>.<MINOR>.<PATCH>` with the setup attached
     (MAJOR.MINOR from `Directory.Build.props`, PATCH automatic). Open: signing, planned with the
     free SignPath Foundation OSS certificate once the repository is public and has a release.
7. **Beyond PLC code (System Manager)** – generic tree tools on ITcSmTreeItem
   (LookupTreeItem, CreateChild, ProduceXml/ConsumeXml, LinkVariables) plus domain skills:
   - I/O / EtherCAT: scan (guarded), add boxes/terminals, PDO config, variable linking
   - Motion NC/PTP: tasks, axes, encoder/drive parameters, axis linking
   - CNC / NCI: channels and groups as far as the Automation Interface exposes them (verify per feature)
   - TwinSAFE: read/inspect + draft only; user reviews, validates and downloads with
     safety password themselves. Never auto-activate or download safety configuration.
   - Tasks, routes, target/AMS Net ID, licenses, ADS settings
   Online actions (activate, restart, login, start/stop) always require explicit confirmation.
