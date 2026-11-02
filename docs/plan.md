# CodeTwin – Plan

## Goal
An extension for TwinCAT XAE Shell (4026, x64) that lets AI agents read, edit and build TwinCAT projects:
an in-process MCP server, a `twincat` agent with skills, a chat window in the shell, and a bridge for
external clients (Copilot CLI and app, Claude Code, Claude Desktop).

## Rules
- Third-party TwinCAT AI tools are a reference for behaviour only. No copied code, prompts or skills.
- Beckhoff: only the public Automation Interface. No Beckhoff DLLs in the repo or the setup.
- InfoSys is Beckhoff copyright: query it live, quote and link it, never crawl or ship an index.
- CodeTwin never handles AI credentials; sign-in is left to Copilot CLI and Claude Code.
- Online actions (activate, login, download, start/stop, restart) always need explicit confirmation.

## Done
- Extension with MCP server: list, read, search, write, create, rename, delete PLC elements,
  PLC visualizations (TcVIS), build, errors, save.
- `twincat` agent and skills, packaged as a Copilot CLI plugin with the `code-twin-mcp` bridge.
- CodeTwin Chat in the shell on GitHub Copilot or Claude: modes, models, permissions, history,
  private chats, attachments.
- Multi-repo sessions through the bridge (`initialize` with several paths).
- Setup (per user), CI with automatic releases, security review.

## Next
1. **Permission modes**: read / edit / build / online, and blocked AMS Net IDs.
2. **System Manager (read first)**: generic tree tools (lookup, read XML), then writes
   (create child, write XML, link variables). Domain skills for:
   - I/O / EtherCAT: scan (guarded), boxes and terminals, PDOs, variable links
   - Motion NC/PTP: tasks, axes, encoder and drive parameters
   - Tasks, routes, target / AMS Net ID, licenses
   - TwinSAFE: read and draft only; the user validates and downloads with the safety password.
3. **More PLC features**: moving elements, library references, running TcUnit tests.
4. **More clients**: Claude Code plugin in the terminal, Claude Desktop (`.mcpb`), Codex, VS Code / Cursor.
5. **Release**: test switching between Copilot and Claude both ways, sign the setup once the repository is public.

## Known issues
- The Copilot app hides the plugin's `codetwin` tools when the `twincat:twincat` agent is selected.
  Workaround: start without an agent and ask it to follow the `twincat` agent.
