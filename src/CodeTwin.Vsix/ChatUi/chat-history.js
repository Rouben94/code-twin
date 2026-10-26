"use strict";

// Chat history: lists the saved CodeTwin chats of the open solution and resumes or deletes them.
(() => {
  const historyButton = document.getElementById("history");
  const panel = document.getElementById("history-panel");
  let sessions = null;

  const relativeTime = (value) => {
    const date = new Date(value);
    const minutes = Math.round((Date.now() - date) / 60000);
    if (isNaN(minutes)) {
      return "";
    }
    if (minutes < 1) {
      return "just now";
    }
    if (minutes < 60) {
      return `${minutes} min ago`;
    }
    if (minutes < 24 * 60) {
      return `${Math.round(minutes / 60)} h ago`;
    }
    if (minutes < 48 * 60) {
      return "yesterday";
    }
    return date.toLocaleDateString(undefined, { month: "short", day: "numeric", year: minutes > 300 * 24 * 60 ? "numeric" : undefined });
  };

  const isOpen = () => !panel.hidden;

  const close = () => {
    panel.hidden = true;
    panel.replaceChildren();
    historyButton.setAttribute("aria-expanded", "false");
  };

  const sessionRow = (session) => {
    const row = element("div", `history-item${session.current ? " current" : ""}`);
    const open = row.appendChild(element("button", "menu-item"));
    open.type = "button";
    open.setAttribute("role", "menuitem");
    open.disabled = busy;
    open.appendChild(element("span", "check", session.current ? "\u2713" : ""));
    const text = open.appendChild(element("span", "menu-text"));
    text.appendChild(element("span", "menu-label history-summary", (session.summary || "Untitled chat").trim()));
    text.appendChild(element("span", "menu-detail", relativeTime(session.modified)));
    open.addEventListener("click", () => {
      close();
      if (!session.current) {
        showTick("warn", "loading chat");
        post({ type: "resumeSession", id: session.id });
      }
      prompt.focus();
    });

    const remove = row.appendChild(element("button", "history-delete"));
    remove.type = "button";
    remove.title = "Delete chat";
    remove.setAttribute("aria-label", "Delete chat");
    remove.disabled = busy;
    remove.innerHTML = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M3 4.5h10M6.5 4.5V3h3v1.5M4.5 4.5l.6 8.5h5.8l.6-8.5"/></svg>';
    remove.addEventListener("click", () => {
      post({ type: "deleteSession", id: session.id });
      sessions = sessions.filter((s) => s.id !== session.id);
      if (session.current) {
        startNewChat(false);
      }
      render();
    });
    return row;
  };

  const render = () => {
    panel.replaceChildren(element("div", "menu-title", "Chat history"));
    if (sessions === null) {
      panel.appendChild(element("div", "menu-empty", "Loading chats..."));
      return;
    }
    if (!sessions.length) {
      panel.appendChild(element("div", "menu-empty", "No saved chats for this solution yet."));
      return;
    }
    if (busy) {
      panel.appendChild(element("div", "menu-empty", "Stop the current reply to switch chats."));
    }
    for (const session of [...sessions].sort((a, b) => new Date(b.modified) - new Date(a.modified))) {
      panel.appendChild(sessionRow(session));
    }
  };

  historyButton.addEventListener("click", () => {
    if (isOpen()) {
      close();
      return;
    }
    sessions = null;
    panel.hidden = false;
    historyButton.setAttribute("aria-expanded", "true");
    render();
    post({ type: "listSessions" });
  });

  handlers.sessions = ({ items }) => {
    sessions = items || [];
    if (isOpen()) {
      render();
    }
  };

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && isOpen()) {
      close();
      historyButton.focus();
    }
  });

  document.addEventListener("pointerdown", (event) => {
    if (isOpen() && !panel.contains(event.target) && !historyButton.contains(event.target)) {
      close();
    }
  });

  window.addEventListener("resize", close);
})();
