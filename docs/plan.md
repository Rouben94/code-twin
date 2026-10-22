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
- Chat UI (decided): WebView2 in a native VS tool window, TypeScript front end; VS theme colors passed to CSS variables and updated on theme change. Agent/skills: Markdown.

## Architecture
```
TcXaeShell (VS 2022 shell)
 └─ CodeTwin.vsix  (.NET Framework 4.8, AsyncPackage, autoload on SolutionExists)
     ├─ TcAutomation core   – DTE/ITcSysManager access, UI-thread marshalling, retry filter
     ├─ MCP server          – Streamable HTTP on 127.0.0.1:<port>/mcp, token auth
     │    tools: list/read/grep/write/replace POU, create POU/DUT/GVL/method/property,
     │           build, errors, save; edits keep the editor open
     ├─ Discovery file      – %LOCALAPPDATA%\CodeTwin\instances\<pid>.json (port + token only)
     └─ Chat tool window    – WebView2 UI ↔ Copilot SDK / CLI sidecar (out-of-proc)
Clients: in-shell chat, Copilot CLI, Copilot Desktop, Claude Desktop (via stdio→HTTP bridge)
```

## Phases
0. **Spike** – minimal VSIX installs into TcXaeShell x64 4026, autoloads, shows a tool window,
   reads the open PLC project tree. Confirms install path/manifest target. (go/no-go)
1. **Core + MCP server** – in-process HTTP MCP server with read tools, then write tools
   (whole-text + find/replace edits, editor kept open), build + error list, save.
   Test with Copilot CLI against a sample project.
2. **Agent + skills** – own `twincat` agent: InfoSys first (live query), web second;
   own skills: naming, ST syntax, OOP/patterns, data types/memory, compiler errors,
   TcUnit, motion. Packaged as a Copilot CLI plugin (marketplace in our repo).
3. **Chat window** – dockable WebView2 chat in the shell, backed by Copilot SDK/CLI sidecar
   with our MCP server + agent preloaded; context = current solution / active POU.
4. **Generic desktop package** – one global Copilot/Claude plugin = agent + skills + MCP config
   pointing to a generic `code-twin-mcp` bridge. Contains NO repository-specific data.
   The project is resolved at call time from the path the desktop app passes
   (`initialize(<repo or .plcproj path>)`): find the .sln/.tsproj in that repo → ask running
   shells (via discovery files) which one has it open → else launch TcXaeShell with it.
   Nothing about the repo is stored or shared between sessions.
5. **Multi-repo use** – in the desktop app add any repo containing a PLC project plus related
   repos; the same agent edits, builds and checks errors in the PLC project through the bridge
   and edits the other repos with normal file tools. Optional per-repo instructions stay in
   that repo (e.g. `.github/copilot-instructions.md`), never in the package.
6. **Hardening** – permission modes (read / edit / build / online), blocked AMS Net IDs,
   no activate/login/start without confirmation, signed VSIX, installer, tests.
7. **Beyond PLC code (System Manager)** – generic tree tools on ITcSmTreeItem
   (LookupTreeItem, CreateChild, ProduceXml/ConsumeXml, LinkVariables) plus domain skills:
   - I/O / EtherCAT: scan (guarded), add boxes/terminals, PDO config, variable linking
   - Motion NC/PTP: tasks, axes, encoder/drive parameters, axis linking
   - CNC / NCI: channels and groups as far as the Automation Interface exposes them (verify per feature)
   - TwinSAFE: read/inspect + draft only; user reviews, validates and downloads with
     safety password themselves. Never auto-activate or download safety configuration.
   - Tasks, routes, target/AMS Net ID, licenses, ADS settings
   Online actions (activate, restart, login, start/stop) always require explicit confirmation.
