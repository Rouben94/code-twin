"use strict";

const host = window.chrome.webview;
const messages = document.getElementById("messages");
const status = document.getElementById("status");
const contextLabel = document.getElementById("context");
const composer = document.getElementById("composer");
const prompt = document.getElementById("prompt");
const sendButton = document.getElementById("send");
const tools = new Map();

let ready = false;
let busy = false;
let reply = null;
let replyText = "";

const post = (message) => host.postMessage(JSON.stringify(message));

const append = (element) => {
  const atBottom = messages.scrollHeight - messages.scrollTop - messages.clientHeight < 40;
  messages.appendChild(element);
  if (atBottom) {
    messages.scrollTop = messages.scrollHeight;
  }
  return element;
};

const element = (tag, className, text) => {
  const node = document.createElement(tag);
  node.className = className;
  if (text !== undefined) {
    node.textContent = text;
  }
  return node;
};

const setBusy = (value) => {
  busy = value;
  sendButton.textContent = busy ? "Stop" : "Send";
  sendButton.classList.toggle("primary", !busy);
};

const endReply = () => {
  reply = null;
  replyText = "";
};

const renderReply = (text) => {
  if (!reply) {
    reply = append(element("div", "message assistant"));
  }
  replyText = text;
  reply.innerHTML = renderMarkdown(replyText);
  messages.scrollTop = messages.scrollHeight;
};

const showContext = (solution, activeElement) => {
  const solutionName = solution ? solution.split("\\").pop() : "No solution open";
  contextLabel.textContent = activeElement ? `${solutionName}  |  ${activeElement}` : solutionName;
  contextLabel.title = [solution, activeElement].filter(Boolean).join("\n");
};

const showPermission = (request) => {
  const card = append(element("div", "permission"));
  card.appendChild(element("div", "", `Allow ${request.kind}?`));
  card.appendChild(element("pre", "", request.detail || ""));
  const answer = (approved) => {
    post({ type: "permission", id: request.id, approved });
    card.replaceChildren(element("div", "note", `${approved ? "Allowed" : "Denied"} ${request.kind}: ${request.detail || ""}`));
  };
  const allow = card.appendChild(element("button", "primary", "Allow"));
  const deny = card.appendChild(element("button", "", "Deny"));
  allow.addEventListener("click", () => answer(true));
  deny.addEventListener("click", () => answer(false));
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
    status.textContent = "Ready. The twincat agent works on the solution open in this shell.";
  },
  session: ({ solution }) => {
    append(element("div", "note", `New session${solution ? ` for ${solution}` : ""}.`));
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
  tool: ({ id, name, status: toolStatus }) => {
    let chip = tools.get(id);
    if (!chip) {
      endReply();
      chip = append(element("span", "tool", name || "tool"));
      tools.set(id, chip);
    }
    chip.className = `tool ${toolStatus}`;
  },
  permission: showPermission,
  idle: () => {
    endReply();
    setBusy(false);
  },
  error: ({ text }) => {
    endReply();
    append(element("div", "message error", text));
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
  append(element("div", "message user", text));
  messages.scrollTop = messages.scrollHeight;
  prompt.value = "";
  setBusy(true);
  post({ type: "send", prompt: text });
});

prompt.addEventListener("keydown", (event) => {
  if (event.key === "Enter" && !event.shiftKey) {
    event.preventDefault();
    composer.requestSubmit();
  }
});

document.getElementById("new-chat").addEventListener("click", () => {
  post({ type: "newChat" });
  messages.replaceChildren(element("div", "note", ready ? "New chat." : "Starting GitHub Copilot..."));
  tools.clear();
  endReply();
  setBusy(false);
});

window.addEventListener("focus", () => post({ type: "getContext" }));

setBusy(false);
post({ type: "ready" });
