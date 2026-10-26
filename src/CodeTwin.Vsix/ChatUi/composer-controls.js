"use strict";

// Mode and model pickers plus the context and usage meter in the composer bar.
(() => {
  const modeButton = document.getElementById("mode");
  const modeText = document.getElementById("mode-text");
  const modelButton = document.getElementById("model");
  const modelText = document.getElementById("model-text");
  const usageButton = document.getElementById("usage");
  const usageArc = document.getElementById("usage-arc");
  const usageText = document.getElementById("usage-text");
  const menu = document.getElementById("menu");

  const modes = [
    { id: "interactive", label: "Agent", detail: "Works step by step with you" },
    { id: "plan", label: "Plan", detail: "Drafts a plan first and changes nothing until you approve it" },
    { id: "autopilot", label: "Autopilot", detail: "Keeps working on its own until the task is done" },
  ];

  const permissionLevels = [
    { id: "ask", label: "Always ask", detail: "Asks before shell commands, file edits and web access. Reads and CodeTwin tools run directly." },
    { id: "approveAll", label: "Approve all", detail: "Runs all tools without asking." },
  ];

  const planActions = {
    interactive: "Start building",
    autopilot: "Build on autopilot",
    exit_only: "Exit plan mode",
  };

  const storage = {
    get: (key) => localStorage.getItem(`codetwin.${key}`),
    set: (key, value) => (value ? localStorage.setItem(`codetwin.${key}`, value) : localStorage.removeItem(`codetwin.${key}`)),
  };

  let models = [];
  let mode = modes.some((m) => m.id === storage.get("mode")) ? storage.get("mode") : "interactive";
  let permissions = permissionLevels.some((p) => p.id === storage.get("permissions")) ? storage.get("permissions") : "ask";
  let model = storage.get("model");
  let effort = storage.get("effort");
  let context = { current: 0, limit: 0 };
  let totals = { input: 0, output: 0, cacheRead: 0, calls: 0 };
  let quota = null;
  let chatCredits = null;
  let openFor = null;

  const formatTokens = (value) => {
    if (value >= 1e6) {
      return `${(value / 1e6).toFixed(1)}M`;
    }
    return value >= 1000 ? `${(value / 1000).toFixed(value >= 1e5 ? 0 : 1)}k` : `${value}`;
  };

  const formatMultiplier = (value) => (value === 0 ? "Included" : value == null ? "" : `${value}x`);

  const findModel = (id) => models.find((m) => m.id === id);

  const showMode = () => {
    const current = modes.find((m) => m.id === mode);
    const level = permissionLevels.find((p) => p.id === permissions);
    modeText.textContent = current.label;
    modeButton.dataset.mode = mode;
    modeButton.dataset.permissions = permissions;
    modeButton.title = `${current.label}: ${current.detail}. Tool permissions: ${level.label} (Shift+Tab to switch mode)`;
  };

  const showModel = () => {
    const current = findModel(model);
    const name = current ? current.name : model || "Default model";
    modelText.textContent = effort && current?.efforts ? `${name} \u00B7 ${effort}` : name;
    modelButton.title = current ? `${current.name} (${formatMultiplier(current.multiplier) || "model"})` : "Model";
  };

  const showUsage = () => {
    const limit = context.limit || findModel(model)?.contextWindow || 0;
    const percent = limit ? Math.min(100, Math.round((context.current / limit) * 100)) : 0;
    usageArc.style.strokeDasharray = `${percent} 100`;
    usageButton.dataset.level = percent >= 90 ? "high" : percent >= 70 ? "medium" : "low";
    usageText.textContent = context.current ? `${percent}%` : "";
    usageButton.title = limit ? `Context ${percent}% used (${formatTokens(context.current)} of ${formatTokens(limit)} tokens)` : "Context and usage";
    if (openFor === usageButton) {
      renderUsagePanel();
    }
  };

  const applyPreferences = () => {
    post({ type: "setMode", mode });
    post({ type: "setPermissions", permissions });
    if (model && findModel(model)) {
      post({ type: "setModel", model, effort });
    }
  };

  const closeMenu = () => {
    menu.hidden = true;
    menu.replaceChildren();
    openFor?.setAttribute("aria-expanded", "false");
    openFor = null;
  };

  const openMenu = (anchor, render) => {
    if (openFor === anchor) {
      closeMenu();
      return;
    }
    closeMenu();
    openFor = anchor;
    anchor.setAttribute("aria-expanded", "true");
    render();
    menu.hidden = false;
    const bar = composer.getBoundingClientRect();
    const box = anchor.getBoundingClientRect();
    const left = Math.max(0, Math.min(box.left - bar.left, bar.width - menu.offsetWidth));
    menu.style.left = `${left}px`;
    menu.querySelector("[aria-checked='true'], button")?.focus();
  };

  const menuItem = (label, detail, meta, checked, onSelect) => {
    const item = element("button", "menu-item");
    item.type = "button";
    item.setAttribute("role", "menuitemradio");
    item.setAttribute("aria-checked", checked ? "true" : "false");
    item.appendChild(element("span", "check", checked ? "\u2713" : ""));
    const text = item.appendChild(element("span", "menu-text"));
    text.appendChild(element("span", "menu-label", label));
    if (detail) {
      text.appendChild(element("span", "menu-detail", detail));
    }
    if (meta) {
      item.appendChild(element("span", "menu-meta", meta));
    }
    item.addEventListener("click", onSelect);
    return item;
  };

  const setMode = (id) => {
    mode = id;
    storage.set("mode", mode);
    showMode();
    post({ type: "setMode", mode });
  };

  const setModel = (id, newEffort) => {
    model = id;
    effort = newEffort;
    storage.set("model", model);
    storage.set("effort", effort);
    showModel();
    showUsage();
    post({ type: "setModel", model, effort });
  };

  const setPermissions = (id) => {
    permissions = id;
    storage.set("permissions", permissions);
    showMode();
    post({ type: "setPermissions", permissions });
  };

  const renderModeMenu = () => {
    menu.className = "menu";
    menu.appendChild(element("div", "menu-title", "Mode"));
    for (const m of modes) {
      menu.appendChild(menuItem(m.label, m.detail, "", m.id === mode, () => {
        setMode(m.id);
        closeMenu();
        prompt.focus();
      }));
    }
    menu.appendChild(element("div", "menu-separator"));
    const level = permissionLevels.find((p) => p.id === permissions);
    const submenu = menu.appendChild(menuItem("Tool permissions", "", `${level.label} \u203A`, false, () => {
      menu.replaceChildren();
      renderPermissionMenu();
      menu.querySelector("[aria-checked='true']")?.focus();
    }));
    submenu.setAttribute("role", "menuitem");
    submenu.setAttribute("aria-haspopup", "menu");
    submenu.removeAttribute("aria-checked");
  };

  const renderPermissionMenu = () => {
    menu.className = "menu";
    menu.appendChild(element("div", "menu-title", "Tool permissions"));
    for (const p of permissionLevels) {
      menu.appendChild(menuItem(p.label, p.detail, "", p.id === permissions, () => {
        setPermissions(p.id);
        closeMenu();
        prompt.focus();
      }));
    }
  };

  const renderModelMenu = () => {
    menu.className = "menu";
    menu.appendChild(element("div", "menu-title", "Model"));
    if (!models.length) {
      menu.appendChild(element("div", "menu-empty", "Models load once GitHub Copilot is connected."));
    }
    for (const m of models) {
      menu.appendChild(menuItem(m.name, "", formatMultiplier(m.multiplier), m.id === model, () => {
        setModel(m.id, m.efforts?.includes(effort) ? effort : null);
        closeMenu();
        prompt.focus();
      }));
    }
    const current = findModel(model);
    if (current?.efforts?.length) {
      menu.appendChild(element("div", "menu-title", "Reasoning effort"));
      const row = menu.appendChild(element("div", "segments"));
      for (const level of current.efforts) {
        const selected = level === (effort || current.defaultEffort);
        const button = row.appendChild(element("button", `segment${selected ? " selected" : ""}`, level));
        button.type = "button";
        button.setAttribute("aria-pressed", selected ? "true" : "false");
        button.addEventListener("click", () => {
          setModel(current.id, level);
          menu.replaceChildren();
          renderModelMenu();
        });
      }
    }
  };

  const meter = (percent) => {
    const bar = element("div", "meter");
    const fill = bar.appendChild(element("span"));
    fill.style.width = `${percent}%`;
    bar.dataset.level = percent >= 90 ? "high" : percent >= 70 ? "medium" : "low";
    return bar;
  };

  const usageRow = (label, value) => {
    const row = element("div", "usage-row");
    row.append(element("span", "", label), element("span", "value", value));
    return row;
  };

  const formatCredits = (value) => value.toLocaleString(undefined, { maximumFractionDigits: value < 100 ? 1 : 0 });

  function renderUsagePanel() {
    menu.replaceChildren();
    menu.className = "menu usage-panel";
    const limit = context.limit || findModel(model)?.contextWindow || 0;
    const percent = limit ? Math.min(100, Math.round((context.current / limit) * 100)) : 0;

    menu.appendChild(element("div", "menu-title", "Context window"));
    menu.appendChild(meter(percent));
    menu.appendChild(usageRow(`${percent}% used`, limit ? `${formatTokens(context.current)} / ${formatTokens(limit)} tokens` : "No session yet"));

    menu.appendChild(element("div", "menu-title", "This chat"));
    menu.appendChild(usageRow("Input tokens", formatTokens(totals.input)));
    menu.appendChild(usageRow("Cached input", formatTokens(totals.cacheRead)));
    menu.appendChild(usageRow("Output tokens", formatTokens(totals.output)));
    menu.appendChild(usageRow("Model calls", `${totals.calls}`));
    if (chatCredits != null) {
      menu.appendChild(usageRow("AI credits", formatCredits(chatCredits)));
    }
    const current = findModel(model);
    if (current) {
      menu.appendChild(usageRow("Model", `${current.name}${current.multiplier != null ? ` \u00B7 ${formatMultiplier(current.multiplier)}` : ""}`));
    }

    if (quota) {
      menu.appendChild(element("div", "menu-title", "Premium requests"));
      if (quota.unlimited) {
        menu.appendChild(usageRow("Plan", "Unlimited"));
      } else {
        const used = Math.max(0, Math.min(100, 100 - quota.remainingPercentage));
        menu.appendChild(meter(Math.round(used)));
        menu.appendChild(usageRow(`${Math.round(quota.used)} of ${quota.entitlement} used`, quota.resetDate
          ? `resets ${new Date(quota.resetDate).toLocaleDateString(undefined, { month: "short", day: "numeric" })}`
          : ""));
      }
    }
  }

  const resetSessionUsage = () => {
    context = { current: 0, limit: 0 };
    totals = { input: 0, output: 0, cacheRead: 0, calls: 0 };
    chatCredits = null;
    showUsage();
  };

  const showPlan = (request) => {
    activity = null;
    endReply();
    ensureTurnHead();
    showTick("warn", "waiting for you");
    const card = append(element("div", "permission plan"));
    card.appendChild(element("div", "title", "Plan ready for review"));
    if (request.summary) {
      card.appendChild(element("div", "message assistant plan-summary")).innerHTML = renderMarkdown(request.summary);
    }
    if (request.plan) {
      const details = card.appendChild(element("details", "plan-content"));
      details.appendChild(element("summary", "", "Show plan"));
      const body = details.appendChild(element("div", "message assistant"));
      body.innerHTML = renderMarkdown(request.plan);
    }
    const actions = card.appendChild(element("div", "actions"));
    const choices = (request.actions || []).filter((action) => planActions[action]);
    if (!choices.length) {
      choices.push(request.recommended || "interactive");
    }
    const answer = (approved, action) => {
      post({ type: "plan", id: request.id, approved, action });
      showTick("warn", "thinking");
      card.className = "answered";
      card.replaceChildren(approved ? `Plan approved: ${planActions[action] || action}` : "Kept planning");
    };
    for (const action of choices) {
      const button = actions.appendChild(element("button", `button${action === request.recommended ? " primary" : ""}`, planActions[action] || action));
      button.addEventListener("click", () => answer(true, action));
    }
    actions.appendChild(element("button", "button", "Keep planning")).addEventListener("click", () => answer(false, null));
    (actions.querySelector(".primary") || actions.firstChild).focus();
  };

  handlers.models = ({ models: list }) => {
    models = list || [];
    showModel();
    showUsage();
    applyPreferences();
  };

  handlers.model = ({ id, effort: newEffort }) => {
    if (!id) {
      return;
    }
    model = id;
    effort = newEffort || effort;
    showModel();
    showUsage();
  };

  handlers.mode = ({ mode: newMode }) => {
    if (modes.some((m) => m.id === newMode)) {
      mode = newMode;
      showMode();
    }
  };

  handlers.contextWindow = ({ current, limit }) => {
    context = { current: current || 0, limit: limit || 0 };
    showUsage();
  };

  handlers.usage = (usage) => {
    totals.input += usage.input || 0;
    totals.output += usage.output || 0;
    totals.cacheRead += usage.cacheRead || 0;
    totals.calls += 1;
    if (!model && usage.model) {
      model = usage.model;
      showModel();
    }
    if (openFor === usageButton) {
      renderUsagePanel();
    }
  };

  handlers.quota = (snapshot) => {
    quota = snapshot;
    if (openFor === usageButton) {
      renderUsagePanel();
    }
  };

  handlers.chatCredits = ({ credits }) => {
    chatCredits = credits;
    if (openFor === usageButton) {
      renderUsagePanel();
    }
  };

  handlers.plan = showPlan;

  handlers.session = ((startSession) => (message) => {
    startSession(message);
    resetSessionUsage();
  })(handlers.session);

  modeButton.addEventListener("click", () => openMenu(modeButton, renderModeMenu));
  modelButton.addEventListener("click", () => openMenu(modelButton, renderModelMenu));
  usageButton.addEventListener("click", () => openMenu(usageButton, renderUsagePanel));

  document.addEventListener("chatreset", () => {
    closeMenu();
    resetSessionUsage();
  });

  prompt.addEventListener("keydown", (event) => {
    if (event.key === "Tab" && event.shiftKey) {
      event.preventDefault();
      const index = modes.findIndex((m) => m.id === mode);
      setMode(modes[(index + 1) % modes.length].id);
    }
  });

  menu.addEventListener("keydown", (event) => {
    const items = [...menu.querySelectorAll("button")];
    const index = items.indexOf(document.activeElement);
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      items[(index + (event.key === "ArrowDown" ? 1 : items.length - 1)) % items.length]?.focus();
    }
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && openFor) {
      const anchor = openFor;
      closeMenu();
      anchor.focus();
    }
  });

  document.addEventListener("pointerdown", (event) => {
    if (openFor && !menu.contains(event.target) && !openFor.contains(event.target)) {
      closeMenu();
    }
  });

  window.addEventListener("resize", closeMenu);

  showMode();
  showModel();
  showUsage();
})();
