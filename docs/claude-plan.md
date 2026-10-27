# Claude support: plan

Goal: someone with a Claude subscription can use CodeTwin Chat the same way someone with a Copilot
subscription does today, and chooses which one the chat uses. Change as little as possible.

## Why this is allowed

Anthropic's [Legal and compliance](https://code.claude.com/docs/en/legal-and-compliance) page forbids
third-party apps to offer Claude.ai sign-in or to handle Claude credentials, but it explicitly does not
prevent "an end user from signing in to the unmodified Claude Code binary with their own Claude
subscription", including where a product runs Claude Code. Conditions:

- Claude Code runs unmodified, as the user installed it, with all its sign-in methods left in place
  (so no `--bare`, which disables subscription sign-in).
- Each user signs in with their own subscription or API key, through Claude Code's own flow.
  CodeTwin never sees a token: the same model as `copilot login` today.
- CodeTwin does not pay for or resell usage, and does not use "Claude Code" in its own product or feature
  names. The chat calls the provider "Claude".
- Running Claude Code in a product requires accepting Anthropic's Commercial Terms. Check with Anthropic
  (contact sales) once before the release; this plan is not legal advice.

Zed and JetBrains do the same: they host the real Claude Code and "the harness owns auth".
Beckhoff's TwinCAT Chat (2023) connects to OpenAI / Azure with API keys and has no Claude option.

## What stays the same

- Chat UI ↔ `code-twin-chat.exe`: the same JSON lines in both directions (`send`, `permission`, `plan`,
  `delta`, `tool`, `idle`, ...).
- The in-shell MCP server, its URL and token, the `plc_*` tools.
- The `twincat` agent and skills in the VSIX `chat` folder. Its `agents/` + `skills/` layout already is
  a Claude Code plugin, so `claude --plugin-dir <chat folder>` loads them unchanged.
- `ChatHost` (Copilot) keeps working as it is.

## What changes

1. **`IChatHost`** (sidecar): the public methods `ChatHost` already has (send, abort, new chat, sessions,
   resume, delete, permission, plan, model, mode, tool permissions, sign-in, auth status). `ChatHost`
   implements it unchanged; `Program.cs` creates the host for the chosen provider.
2. **`ClaudeChatHost`** (new, sidecar): runs the user's `claude.exe` (from `PATH`, `%USERPROFILE%\.local\bin`
   or `CLAUDE_CLI_PATH`, like `CopilotCliLocator`) with
   `-p --input-format stream-json --output-format stream-json --verbose --include-partial-messages
   --plugin-dir <chat folder> --agent twincat --mcp-config <codetwin HTTP + bearer token>
   --append-system-prompt <solution context> --model --effort --permission-mode`, working directory = solution folder.
   - stream events → `delta`, `message`, `tool`, `usage`, `contextWindow`, `idle`
   - permission requests → the existing permission card; `ExitPlanMode` → the existing plan card.
     Through the stdin control protocol the Agent SDKs use (`--permission-prompt-tool stdio`);
     fallback: the documented `--permission-prompt-tool` with an MCP tool. The spike decides.
   - modes: Interactive → `default`, Plan → `plan`, Autopilot → `acceptEdits`
   - trusted reads, approve-all and the network-path rule: the same `IsTrusted` logic
   - sessions: `--resume <id>`; the list comes from the transcripts in `~/.claude/projects/<solution folder>/`,
     filtered to the ids CodeTwin started; private chats are deleted the same way
   - sign-in: `claude auth login` in its own console window, like `copilot login`; status from `claude auth status`
   - models: Claude Code has no model list call, so the picker offers the aliases (Opus, Sonnet, Haiku, Fable)
     with their efforts
   - quota and AI credits: no source for subscriptions, so the usage menu shows only the context window
3. **Provider option**, done: Tools > Options > CodeTwin > Chat, stored per user, default GitHub Copilot.
   - **AI provider** (GitHub Copilot / Claude) and an optional **Claude Code path**; the extension passes them
     to the sidecar as `CODETWIN_CHAT_PROVIDER` and `CLAUDE_CLI_PATH`
   - changing the provider restarts the sidecar and clears the chat (chats don't move between providers);
     the chat history shows only the active provider's chats
   - the status line, sign-in card and private-chat note name the active provider
4. **Chat UI**, done: provider name in the texts; the effort row is pinned in the model menu; on Claude the
   usage panel shows the 5-hour and weekly limits from Claude Code's `rate_limit_event` instead of the
   Copilot quota.

Found while testing in the shell: Claude Code validates MCP results, and `structuredContent` must be an
object. List results (`plc_list_elements`, `plc_search_elements`, `plc_get_errors`) are now sent as
`{"items": [...]}`; the text content is unchanged.

## Spike results (2026-10-27, Claude Code 2.1.308, Pro subscription)

Driver: one `claude.exe` process per chat, started with
`-p --input-format stream-json --output-format stream-json --verbose --include-partial-messages
--permission-prompt-tool stdio --plugin-dir <chat folder> --agent chat:twincat --mcp-config <json>
--strict-mcp-config --model <id>`. Prompts and control requests are JSON lines on stdin.

| Check | Result |
| --- | --- |
| Agent and skills from the VSIX `chat` folder, unchanged | OK. Plugin name = folder name, so the agent is `chat:twincat` and skills `chat:<name>`. A `.claude-plugin/plugin.json` (`"name": "codetwin"`) gives nicer names. |
| `codetwin` tools from `--mcp-config` | OK for stdio (`mcp__codetwin__*`). The HTTP entry with `Authorization: Bearer` is accepted; not yet tested against a running shell. |
| Streaming | OK: `stream_event` / `content_block_delta` → `delta`, `assistant` → `message`, `result` → `idle` with usage and cost. |
| Permissions | OK: `control_request` `can_use_tool` (tool name, input, description, reason) → `control_response` `allow` (with `updatedInput`) or `deny` (with a message the model reads). |
| Plan mode | OK: `--permission-mode plan`; the plan arrives as `can_use_tool` for `ExitPlanMode` with `input.plan`. Allow leaves plan mode, deny with feedback keeps it. |
| Mode and model while running | OK: `set_permission_mode` (`default`, `plan`, `acceptEdits`), `set_model` (alias or id). |
| Abort | OK: `interrupt`; the turn ends with `result` `error_during_execution` / `aborted_streaming` (show as idle, not as error). |
| Multi-turn in one process | OK: the next user line after `result` continues the same session. |
| Session ids | OK: `--session-id <uuid>` sets CodeTwin's own id, `--resume <id>` continues it (also in a new process). Transcripts: `~/.claude/projects/<cwd with separators as '-'>/<id>.jsonl`. |
| Models | `initialize` returns the list (Default, Opus, Fable, Sonnet, Haiku) and the account. |
| Sign-in | `claude auth status --json` → `loggedIn`, `authMethod`, `subscriptionType`. `claude auth login` signs in through Anthropic's flow. |

Findings for the implementation:

- `auth status` can say `loggedIn` while the token has expired; the first request then fails with
  `error: authentication_failed`. Treat that as "show the sign-in card".
- The Claude desktop app's bundled `claude.exe` is not usable: it lives in the MSIX package folder
  (`%LOCALAPPDATA%\Packages\Claude_*\LocalCache\...`, versioned, hash-named). Locate a user-installed
  Claude Code (`CLAUDE_CLI_PATH`, `PATH`, `%USERPROFILE%\.local\bin\claude.exe`), like `CopilotCliLocator`.
- The user's own `~/.claude` settings, skills and plugins also load (as Copilot CLI loads its config).
  Keep it that way; `--setting-sources` can narrow it later if it gets in the way.
- Claude Code asks for reads on short 8.3 paths (`CHBOB~1`), a spike artifact of the temp folder; the
  existing `IsTrusted` rule answers reads in the solution folder without asking anyway.
- Transcript files are Claude Code's internal format. For the history list read only the `user` /
  `assistant` text entries (title = first prompt) and tolerate anything else.
- `ExitPlanMode` has no action list like Copilot's; the plan card shows approve / reject with feedback.

The HTTP MCP endpoint against a running TcXaeShell was confirmed in the first test of the implementation.

## Order

1. Spike, done.
2. `IChatHost` and `ClaudeChatHost`, with tests like `ChatHost`'s, done.
3. Provider option and the UI texts, done.
4. Manual test with a Claude subscription and a Copilot subscription on the same machine, switching both ways:
   chat on Claude tested in the shell; switching both ways still to test.

Later, each small: the Claude Code plugin for the terminal (`.claude-plugin/plugin.json` next to Copilot's,
registered by setup), and Claude Desktop (`.mcpb` bundle). Then the new PLC tools on the Automation Interface.
