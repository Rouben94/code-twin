<h1 align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/codetwin-logo-dark.svg">
    <img src="assets/codetwin-logo-light.svg" alt="CodeTwin" width="480">
  </picture>
</h1>

AI engineering for TwinCAT: AI agents read, edit and build your TwinCAT PLC code in TwinCAT XAE Shell.

## How it works

```mermaid
flowchart LR
    chat["CodeTwin Chat<br/>(in the shell)"] --> mcp
    cli["Copilot CLI /<br/>Copilot app"] -- "twincat plugin<br/>(code-twin-mcp)" --> mcp
    subgraph shell["TwinCAT XAE Shell"]
        mcp["CodeTwin extension<br/>(MCP server)"] --> ai["TwinCAT<br/>Automation Interface"]
    end
```

- The **CodeTwin extension** runs inside TwinCAT XAE Shell and gives AI agents tools to list, read, write, create and build PLC elements.
- The **chat** inside the shell talks to it directly. It runs on Copilot CLI or on Claude Code.
- **Copilot CLI** and the **Copilot app** reach it through the `twincat` plugin. The plugin finds the shell that has your project open, or starts one.
- The AI runs on your own GitHub Copilot or Claude subscription. CodeTwin never touches your credentials.

## Install

You need Windows x64, TwinCAT XAE Shell (TwinCAT 3.1.4026), the [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) and [Copilot CLI](https://docs.github.com/copilot/how-tos/copilot-cli) or [Claude Code](https://code.claude.com/docs/en/setup).

1. Close TwinCAT XAE Shell.
2. Run `CodeTwin-Setup-<version>.exe` from the [releases](https://github.com/Rouben94/code-twin/releases). No admin rights needed.

To update, run a newer setup. To uninstall, use **Settings > Apps**.

## Use

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/chat-dark.png">
    <img src="assets/chat-light.png" alt="CodeTwin Chat in TwinCAT XAE Shell" width="720">
  </picture>
</p>

- **In the shell:** open your solution, then **Tools > CodeTwin > Chat**. The chat uses GitHub Copilot; to use your Claude subscription instead, set **AI provider** to Claude in **Tools > CodeTwin > Settings**.
- **In Copilot CLI:** run `copilot --agent twincat:twincat` in your project folder.
- **In the Copilot app:** open your project folder and ask it to follow the `twincat` agent.

Then just ask, for example:

- `Implement F_Add with two INT inputs and call it from MAIN, then build and fix all errors.`
- `Write TcUnit tests for FB_Valve.`
- `What does compiler error C0077 mean?`

Overview and how to build CodeTwin: [docs/overview.md](docs/overview.md).

## What is supported

Supported today:

- Listing, searching and reading PLC elements
- Changing Structured Text code (declaration and implementation)
- Creating folders, programs, function blocks, functions, interfaces, methods, properties, actions, structs, enums, unions, aliases, GVLs and PLC visualizations
- Renaming and deleting PLC elements
- Reading and changing PLC visualizations (also served as web visu) as TcVIS XML
- Building the solution and fixing compiler errors and warnings
- Writing TcUnit tests (running them is up to you)
- Several solutions or repositories open at the same time, each in its own shell
- Clients: CodeTwin Chat (on GitHub Copilot or Claude), Copilot CLI and the Copilot app
- TwinCAT 3.1.4026

Not supported yet:

- Moving PLC elements
- Graphical languages (FBD, LD, SFC, CFC)
- TwinCAT HMI (TE2000)
- I/O tree, devices and EtherCAT configuration
- Motion (NC axes) and TwinCAT Safety configuration
- Adding or changing library references
- Activating the configuration, logging in, downloading and online changes
- Running TcUnit tests
- Clients: Claude Code in the terminal, VS Code, Cursor, Codex CLI and Claude Desktop
- TwinCAT 3.1.4024

More is coming, and this list is updated as features are added.

Licensed under [MPL-2.0](LICENSE).
