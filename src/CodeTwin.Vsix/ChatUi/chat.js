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
const tools = new Map();

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

const post = (message) => host.postMessage(JSON.stringify(message));

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

const renderReply = (text) => {
  const follow = isNearBottom();
  if (!reply) {
    activity = null;
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

const handlers = {
  theme: ({ colors }) => {
    for (const [name, value] of Object.entries(colors)) {
      document.documentElement.style.setProperty(`--${name.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`)}`, value);
    }
  },
  context: ({ solution, activeElement }) => showContext(solution, activeElement),
  ready: () => {
    ready = true;
    setStatus("online", "twincat agent");
  },
  session: () => {
    if (thread.querySelector(".message.assistant")) {
      append(element("div", "divider", "New session"));
    }
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
  },
  error: ({ text }) => {
    if (!ready) {
      setStatus("offline", "Copilot not connected");
    }
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
  append(element("div", "message user", text));
  messages.scrollTop = messages.scrollHeight;
  prompt.value = "";
  autosize();
  setBusy(true);
  post({ type: "send", prompt: text });
});

prompt.addEventListener("input", autosize);

prompt.addEventListener("keydown", (event) => {
  if (event.key === "Enter" && !event.shiftKey) {
    event.preventDefault();
    composer.requestSubmit();
  }
});

for (const suggestion of document.querySelectorAll(".suggestion")) {
  suggestion.addEventListener("click", () => {
    prompt.value = suggestion.dataset.prompt;
    autosize();
    prompt.focus();
  });
}

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

document.getElementById("new-chat").addEventListener("click", () => {
  post({ type: "newChat" });
  thread.replaceChildren();
  welcome.hidden = false;
  tools.clear();
  endReply();
  activity = null;
  setBusy(false);
  prompt.focus();
});

window.addEventListener("focus", () => post({ type: "getContext" }));

setBusy(false);
prompt.focus();
post({ type: "ready" });
