"use strict";

const host = window.chrome.webview;
const messages = document.getElementById("messages");
const thread = document.getElementById("thread");
const welcome = document.getElementById("welcome");
const welcomeContext = document.getElementById("welcome-context");
const typing = document.getElementById("typing");
const status = document.getElementById("status");
const statusText = document.getElementById("status-text");
const contextPill = document.getElementById("context");
const contextText = document.getElementById("context-text");
const composer = document.getElementById("composer");
const prompt = document.getElementById("prompt");
const sendButton = document.getElementById("send");
const tickStatus = document.getElementById("tick-status");
const tickStatusText = document.getElementById("tick-status-text");
const tools = new Map();

document.getElementById("tick-large").appendChild(TickSprite.create(7));

const toolLabels = {
  plc_list_elements: "List elements",
  plc_read_element: "Read element",
  plc_search_elements: "Search elements",
  plc_write_element: "Write element",
  plc_replace_element_text: "Edit element",
  plc_create_element: "Create element",
  plc_save_solution: "Save solution",
  plc_build: "Build solution",
  plc_get_errors: "Read error list",
};

const permissionTitles = {
  shell: "Run a shell command?",
  write: "Write a file?",
  url: "Open a URL?",
  mcp: "Call an MCP tool?",
  memory: "Store a memory?",
};

let ready = false;
let busy = false;
let reply = null;
let replyText = "";
let activity = null;
let turnHeadShown = false;

const post = (message) => host.postMessage(JSON.stringify(message));

const tickLabels = { run: "RUN", warn: "WARNING", error: "ERROR" };

const showTick = (state, text) => {
  setTickState(state);
  tickStatus.style.color = TickSprite.eyeColor(state);
  tickStatusText.textContent = `${tickLabels[state]} \u00B7 ${text}`;
};

const isNearBottom = () => messages.scrollHeight - messages.scrollTop - messages.clientHeight < 80;

const scrollIfFollowing = (follow) => {
  if (follow) {
    messages.scrollTop = messages.scrollHeight;
  }
};

const element = (tag, className, text) => {
  const node = document.createElement(tag);
  if (className) {
    node.className = className;
  }
  if (text !== undefined) {
    node.textContent = text;
  }
  return node;
};

const append = (node) => {
  const follow = isNearBottom();
  welcome.hidden = true;
  thread.appendChild(node);
  scrollIfFollowing(follow);
  return node;
};

const setStatus = (state, text) => {
  status.className = `status ${state}`;
  statusText.textContent = text;
};

const updateSendButton = () => {
  sendButton.classList.toggle("busy", busy);
  sendButton.title = busy ? "Stop" : "Send";
  sendButton.setAttribute("aria-label", sendButton.title);
  sendButton.disabled = !busy && prompt.value.trim() === "";
};

const setBusy = (value) => {
  busy = value;
  const follow = isNearBottom();
  typing.hidden = !busy;
  scrollIfFollowing(follow);
  updateSendButton();
};

const endReply = () => {
  reply = null;
  replyText = "";
};

const ensureTurnHead = () => {
  if (!turnHeadShown) {
    turnHeadShown = true;
    const head = element("div", "turn-head");
    head.append(TickSprite.create(2), "Tick");
    append(head);
  }
};

const renderReply = (text) => {
  const follow = isNearBottom();
  if (!reply) {
    activity = null;
    ensureTurnHead();
    reply = append(element("div", "message assistant"));
  }
  replyText = text;
  reply.innerHTML = renderMarkdown(replyText);
  scrollIfFollowing(follow);
};

const lastSegment = (path) => path.split(/[\\/]/).filter(Boolean).pop() || path;

const showContext = (solution, activeElement) => {
  contextText.replaceChildren();
  if (!solution) {
    contextText.textContent = "No solution open";
  } else {
    contextText.append(lastSegment(solution));
    if (activeElement) {
      contextText.append("  \u203A  ", element("strong", "", lastSegment(activeElement.replace(/^File \((.*)\)$/, "$1"))));
    }
  }
  contextPill.title = [solution, activeElement].filter(Boolean).join("\n");
  welcomeContext.textContent = solution
    ? `Working on ${lastSegment(solution)}${activeElement ? `, focused on ${lastSegment(activeElement)}` : ""}. The twincat agent reads, edits and builds the PLC project for you.`
    : "Open a TwinCAT solution in this shell, then ask the twincat agent to read, edit or build its PLC code.";
};

const summarize = (block) => {
  const states = [...block.items.values()].map((item) => item.state);
  const running = [...block.items.values()].find((item) => item.state === "running");
  const failed = states.filter((state) => state === "failed").length;
  block.icon.className = `state ${running ? "running" : failed ? "failed" : "done"}`;
  if (running) {
    block.label.textContent = `${running.label}...`;
  } else {
    const count = states.length;
    block.label.textContent = `Used ${count} tool${count === 1 ? "" : "s"}${failed ? `, ${failed} failed` : ""}`;
  }
};

const startActivity = () => {
  ensureTurnHead();
  const details = element("details", "activity");
  const summary = details.appendChild(element("summary"));
  const icon = summary.appendChild(element("span", "state running"));
  const label = summary.appendChild(element("span"));
  summary.insertAdjacentHTML("beforeend", '<svg class="chevron" viewBox="0 0 16 16" aria-hidden="true"><path d="M6 3.5 10.5 8 6 12.5"/></svg>');
  const list = details.appendChild(element("ul"));
  append(details);
  return { details, icon, label, list, items: new Map() };
};

const showTool = ({ id, name, status: toolStatus }) => {
  let item = tools.get(id);
  if (!item) {
    endReply();
    activity = activity || startActivity();
    const label = toolLabels[name] || name || "Tool";
    const row = activity.list.appendChild(element("li"));
    const icon = row.appendChild(element("span", "state"));
    row.appendChild(element("span", "", label));
    if (toolLabels[name]) {
      row.appendChild(element("span", "name", name));
    }
    item = { block: activity, icon, label, state: toolStatus };
    activity.items.set(id, item);
    tools.set(id, item);
  }
  item.state = toolStatus;
  item.icon.className = `state ${toolStatus}`;
  summarize(item.block);
};

const showPermission = (request) => {
  activity = null;
  ensureTurnHead();
  showTick("warn", "waiting for you");
  const card = append(element("div", "permission"));
  card.appendChild(element("div", "title", permissionTitles[request.kind] || `Allow ${request.kind}?`));
  if (request.detail) {
    card.appendChild(element("pre", "", request.detail));
  }
  const actions = card.appendChild(element("div", "actions"));
  const allow = actions.appendChild(element("button", "button primary", "Allow"));
  const deny = actions.appendChild(element("button", "button", "Deny"));
  const answer = (approved) => {
    post({ type: "permission", id: request.id, approved });
    showTick("warn", "thinking");
    card.className = "answered";
    card.replaceChildren(`${approved ? "Allowed" : "Denied"}: ${request.detail || request.kind}`);
  };
  allow.addEventListener("click", () => answer(true));
  deny.addEventListener("click", () => answer(false));
  allow.focus();
};

const showError = (text) => {
  endReply();
  activity = null;
  append(element("div", "message error", text));
};

let signInCard = null;

// The provider set in Tools > Options > CodeTwin > Chat; the sidecar names it in its ready message.
let provider = "GitHub Copilot";

// Sign-in runs in the provider's own window (`copilot login`, `claude auth login`); the chat only starts it.
const showSignedOut = () => {
  setStatus("offline", "Not signed in");
  showTick("error", "not signed in");
  signInCard = append(element("div", "permission"));
  signInCard.appendChild(element("div", "title", `Sign in to ${provider}`));
  signInCard.appendChild(element("div", "", `The chat uses your ${provider} account. Sign-in opens in a ${provider} window; CodeTwin never sees your credentials.`));
  const actions = signInCard.appendChild(element("div", "actions"));
  const button = actions.appendChild(element("button", "button primary", "Sign in"));
  button.addEventListener("click", () => {
    button.disabled = true;
    button.textContent = "Waiting for sign-in...";
    post({ type: "signIn" });
  });
};

const handlers = {
  theme: ({ colors }) => {
    for (const [name, value] of Object.entries(colors)) {
      document.documentElement.style.setProperty(`--${name.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`)}`, value);
    }
  },
  context: ({ solution, activeElement }) => showContext(solution, activeElement),
  ready: (message) => {
    ready = true;
    provider = message.provider || provider;
    document.getElementById("private-provider").textContent = provider;
    setStatus("online", "twincat agent");
    if (!busy) {
      showTick("run", "ready");
    }
  },
  auth: ({ signedIn }) => {
    signInCard?.remove();
    signInCard = null;
    if (!signedIn) {
      showSignedOut();
      return;
    }
    setStatus("online", "twincat agent");
    if (!busy) {
      showTick("run", "ready");
    }
  },
  session: ({ private: isPrivate }) => {
    setPrivate(Boolean(isPrivate));
    if (thread.querySelector(".message.assistant")) {
      append(element("div", "divider", "New session"));
    }
  },
  history: ({ items }) => {
    setPrivate(false);
    replayHistory(items || []);
  },
  delta: ({ text }) => renderReply(replyText + (text || "")),
  message: ({ text }) => {
    if (text) {
      renderReply(text);
    } else if (reply && !replyText) {
      reply.remove();
    }
    endReply();
  },
  tool: showTool,
  permission: showPermission,
  idle: () => {
    endReply();
    activity = null;
    setBusy(false);
    if (TickSprite.state !== "error") {
      showTick("run", "ready");
    }
  },
  error: ({ text }) => {
    if (!ready) {
      setStatus("offline", "Chat not connected");
    }
    showTick("error", ready ? "request failed" : "not connected");
    showError(text);
    setBusy(false);
  },
};

host.addEventListener("message", (event) => {
  const message = event.data;
  const handler = handlers[message.type];
  if (handler) {
    handler(message);
  }
});

const autosize = () => {
  prompt.style.height = "auto";
  prompt.style.height = `${prompt.scrollHeight}px`;
  prompt.style.overflowY = prompt.scrollHeight > prompt.clientHeight ? "auto" : "hidden";
  updateSendButton();
};

composer.addEventListener("submit", (event) => {
  event.preventDefault();
  if (busy) {
    post({ type: "abort" });
    return;
  }

  const text = prompt.value.trim();
  if (!text) {
    return;
  }

  endReply();
  activity = null;
  turnHeadShown = false;
  append(element("div", "message user", text));
  messages.scrollTop = messages.scrollHeight;
  prompt.value = "";
  autosize();
  setBusy(true);
  showTick("warn", "thinking");
  post({ type: "send", prompt: text });
});

prompt.addEventListener("input", autosize);

prompt.addEventListener("keydown", (event) => {
  if (event.key === "Enter" && !event.shiftKey) {
    event.preventDefault();
    composer.requestSubmit();
  }
});

thread.addEventListener("click", async (event) => {
  const button = event.target.closest(".copy");
  if (!button) {
    return;
  }
  try {
    await navigator.clipboard.writeText(button.closest(".code").querySelector("code").textContent);
    button.textContent = "Copied";
  } catch {
    button.textContent = "Copy failed";
  }
  setTimeout(() => { button.textContent = "Copy"; }, 1500);
});

const privateButton = document.getElementById("private-chat");

const setPrivate = (isPrivate) => {
  document.body.classList.toggle("private", isPrivate);
  TickSprite.setShades(isPrivate);
  privateButton.setAttribute("aria-pressed", isPrivate ? "true" : "false");
  privateButton.title = isPrivate ? "Leave private chat (it is deleted) and start a saved chat" : "New private chat (not saved)";
  privateButton.setAttribute("aria-label", privateButton.title);
};

const resetThread = () => {
  thread.replaceChildren();
  welcome.hidden = false;
  tools.clear();
  endReply();
  activity = null;
  turnHeadShown = false;
  setBusy(false);
  showTick(ready ? "run" : "warn", ready ? "ready" : "connecting");
  document.dispatchEvent(new Event("chatreset"));
};

// The extension restarts the sidecar on the new provider; the old chat belongs to the old provider.
handlers.providerChanged = () => {
  ready = false;
  signInCard?.remove();
  signInCard = null;
  resetThread();
  setPrivate(false);
  setStatus("connecting", "Switching provider...");
};

const startNewChat = (isPrivate) => {
  post({ type: "newChat", private: isPrivate });
  resetThread();
  setPrivate(isPrivate);
  prompt.focus();
};

const replayHistory = (items) => {
  resetThread();
  for (const item of items) {
    if (item.kind === "user") {
      endReply();
      activity = null;
      turnHeadShown = false;
      append(element("div", "message user", item.text));
    } else if (item.kind === "assistant") {
      renderReply(item.text);
      endReply();
    } else if (item.kind === "tool") {
      showTool(item);
    }
  }
  endReply();
  activity = null;
  for (const item of tools.values()) {
    if (item.state === "running") {
      item.state = "failed";
      item.icon.className = "state failed";
      summarize(item.block);
    }
  }
  messages.scrollTop = messages.scrollHeight;
};

document.getElementById("new-chat").addEventListener("click", () => startNewChat(false));
privateButton.addEventListener("click", () => startNewChat(!document.body.classList.contains("private")));

window.addEventListener("focus", () => post({ type: "getContext" }));

setBusy(false);
showTick("warn", "connecting");
prompt.focus();
post({ type: "ready" });
