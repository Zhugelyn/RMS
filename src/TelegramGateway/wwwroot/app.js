(() => {
  const tg = window.Telegram?.WebApp;
  tg?.ready();
  tg?.expand();

  // Theme: Telegram themeParams + leaf palette fallbacks.
  const tp = tg?.themeParams || {};
  const root = document.documentElement;
  if (tp.bg_color) root.style.setProperty("--tg-bg", tp.bg_color);
  if (tp.text_color) root.style.setProperty("--tg-text", tp.text_color);
  if (tp.hint_color) root.style.setProperty("--tg-hint", tp.hint_color);
  if (tp.button_color) root.style.setProperty("--tg-button", tp.button_color);
  if (tp.secondary_bg_color) root.style.setProperty("--tg-secondary", tp.secondary_bg_color);
  try {
    tg?.setHeaderColor?.(tp.bg_color || "#0f3d2b");
    tg?.setBackgroundColor?.(tp.secondary_bg_color || tp.bg_color || "#f3f7f2");
  } catch (_) { /* older clients */ }

  const copy = {
    salon: {
      title: "Салон",
      text: "Записи, расписание, клиенты — intent=salon."
    },
    marketing: {
      title: "Маркетинг",
      text: "Спроси ассистента (intent=marketing). Студия research — ниже."
    },
    tasks: {
      title: "Задачи",
      text: "Планирование и встречи — intent=tasks."
    }
  };

  const panel = document.getElementById("panel");
  const panelTitle = document.getElementById("panel-title");
  const panelCopy = document.getElementById("panel-copy");
  const form = document.getElementById("chat-form");
  const message = document.getElementById("message");
  const status = document.getElementById("status");
  const reply = document.getElementById("reply");
  const buttons = [...document.querySelectorAll(".domain")];

  const researchPanel = document.getElementById("research-panel");
  const researchForm = document.getElementById("research-form");
  const researchEnabled = document.getElementById("research-enabled");
  const researchHandle = document.getElementById("research-handle");
  const researchTz = document.getElementById("research-tz");
  const researchCadence = document.getElementById("research-cadence");
  const researchLast = document.getElementById("research-last");
  const researchNext = document.getElementById("research-next");
  const researchErrorLine = document.getElementById("research-error-line");
  const researchStatus = document.getElementById("research-status");
  const researchRunBtn = document.getElementById("research-run");
  const studioHandle = document.getElementById("studio-handle");
  const studioEnabled = document.getElementById("studio-enabled");
  const analyticsGrid = document.getElementById("analytics-grid");
  const analyticsEmpty = document.getElementById("analytics-empty");
  const planGallery = document.getElementById("plan-gallery");
  const galleryEmpty = document.getElementById("gallery-empty");

  let activeIntent = null;
  let conversationId = `mini-${crypto.randomUUID()}`;
  const blobUrls = [];

  function initDataHeaders() {
    const headers = { "Content-Type": "application/json" };
    if (tg?.initData) {
      headers["X-Telegram-Init-Data"] = tg.initData;
    }
    return headers;
  }

  function resolveUserId() {
    const id = tg?.initDataUnsafe?.user?.id;
    return id ? `tg-${id}` : null;
  }

  function resolveNotifyChatId() {
    const id = tg?.initDataUnsafe?.user?.id;
    return id ? String(id) : null;
  }

  function fmt(iso) {
    if (!iso) return "—";
    try {
      return new Date(iso).toISOString().replace("T", " ").slice(0, 16) + "Z";
    } catch {
      return String(iso);
    }
  }

  function paintSettings(s) {
    researchEnabled.checked = !!s.enabled;
    researchHandle.value = s.instagramHandle ? `@${s.instagramHandle}` : "";
    researchTz.value = s.timezone || "";
    researchCadence.value = String(s.cadenceDays || 14);
    researchLast.textContent = fmt(s.lastRunAt);
    researchNext.textContent = fmt(s.nextRunAt);
    researchErrorLine.textContent = "last error: " + (s.lastError || "—");
    studioHandle.textContent = s.instagramHandle ? `@${s.instagramHandle}` : "@—";
    studioEnabled.textContent = s.enabled ? "on" : "off";
    studioEnabled.dataset.on = s.enabled ? "1" : "0";
  }

  function paintAnalytics(analytics) {
    analyticsGrid.innerHTML = "";
    const noInsights = !analytics || (
      analytics.impressions == null &&
      analytics.reach == null &&
      analytics.engagement == null &&
      analytics.saved == null
    );
    if (noInsights) {
      analyticsEmpty.hidden = false;
      return;
    }
    analyticsEmpty.hidden = true;
    const cards = [
      ["impressions", analytics.impressions],
      ["reach", analytics.reach],
      ["engagement", analytics.engagement],
      ["saved", analytics.saved],
      ["posts", analytics.postCount],
      ["captured", analytics.capturedAt ? fmt(analytics.capturedAt) : "—"]
    ];
    for (const [label, value] of cards) {
      const el = document.createElement("div");
      el.className = "analytics-card";
      el.innerHTML = `<span class="analytics-label">${label}</span><span class="analytics-value">${value ?? "—"}</span>`;
      analyticsGrid.appendChild(el);
    }
  }

  function revokeBlobs() {
    while (blobUrls.length) {
      URL.revokeObjectURL(blobUrls.pop());
    }
  }

  async function loadMediaBlob(imageUrl) {
    if (!imageUrl || !tg?.initData) return null;
    try {
      const res = await fetch(imageUrl, {
        headers: { "X-Telegram-Init-Data": tg.initData }
      });
      if (!res.ok) return null;
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      blobUrls.push(url);
      return url;
    } catch {
      return null;
    }
  }

  async function paintGallery(items) {
    revokeBlobs();
    planGallery.innerHTML = "";
    if (!items || items.length === 0) {
      galleryEmpty.hidden = false;
      return;
    }
    galleryEmpty.hidden = true;

    for (const item of items) {
      const card = document.createElement("article");
      card.className = "plan-card";
      card.dataset.status = item.status || "draft";

      const media = document.createElement("div");
      media.className = "plan-media";
      if (item.imageUrl) {
        const src = await loadMediaBlob(item.imageUrl);
        if (src) {
          const img = document.createElement("img");
          img.alt = item.date || "plan";
          img.loading = "lazy";
          img.src = src;
          media.appendChild(img);
        } else {
          media.classList.add("is-empty");
          media.textContent = "нет фото";
        }
      } else {
        media.classList.add("is-empty");
        media.textContent = "нет фото";
      }

      const body = document.createElement("div");
      body.className = "plan-body";
      const tags = (item.hashtags || []).map((h) => (h.startsWith("#") ? h : "#" + h)).join(" ");
      body.innerHTML = `
        <div class="plan-meta">
          <time>${item.date || "—"}</time>
          <span class="plan-status">${item.status || "draft"}</span>
        </div>
        <p class="plan-caption">${escapeHtml(item.caption || "")}</p>
        <p class="plan-tags">${escapeHtml(tags)}</p>
        <div class="plan-prompt">
          <code class="plan-prompt-text">${escapeHtml(item.imagePrompt || "")}</code>
          <button type="button" class="copy-btn" data-copy="${escapeAttr(item.imagePrompt || "")}">copy</button>
        </div>`;

      card.appendChild(media);
      card.appendChild(body);
      planGallery.appendChild(card);
    }
  }

  function escapeHtml(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function escapeAttr(s) {
    return escapeHtml(s).replace(/'/g, "&#39;");
  }

  planGallery.addEventListener("click", async (ev) => {
    const btn = ev.target.closest("[data-copy]");
    if (!btn) return;
    const text = btn.getAttribute("data-copy") || "";
    try {
      await navigator.clipboard.writeText(text);
      btn.textContent = "ok";
      setTimeout(() => { btn.textContent = "copy"; }, 900);
    } catch {
      btn.textContent = "err";
    }
  });

  async function loadResearch() {
    const userId = resolveUserId();
    if (!userId) {
      researchStatus.textContent = "Открой Mini App из Telegram (нужен tg userId).";
      return;
    }

    researchStatus.textContent = "Загружаю…";
    researchPanel.classList.add("is-loading");
    try {
      const [settingsRes, latestRes] = await Promise.all([
        fetch(`/api/miniapp/research/settings?userId=${encodeURIComponent(userId)}`),
        fetch(`/api/miniapp/research/latest?userId=${encodeURIComponent(userId)}`)
      ]);
      if (!settingsRes.ok) {
        const err = await settingsRes.json().catch(() => ({}));
        throw new Error(err.detail || err.title || `HTTP ${settingsRes.status}`);
      }
      const settings = await settingsRes.json();
      paintSettings(settings);

      if (latestRes.ok) {
        const latest = await latestRes.json();
        if (latest.settings) paintSettings(latest.settings);
        paintAnalytics(latest.analytics);
        await paintGallery(latest.items || []);
      } else {
        paintAnalytics(null);
        await paintGallery([]);
      }
      researchStatus.textContent = "";
      researchPanel.classList.remove("is-error");
    } catch (error) {
      researchStatus.textContent = error.message || "Ошибка загрузки";
      researchPanel.classList.add("is-error");
      paintAnalytics(null);
      await paintGallery([]);
    } finally {
      researchPanel.classList.remove("is-loading");
    }
  }

  buttons.forEach((btn) => {
    btn.addEventListener("click", () => {
      activeIntent = btn.dataset.intent;
      buttons.forEach((b) => b.classList.toggle("is-active", b === btn));
      const meta = copy[activeIntent];
      const showResearch = activeIntent === "marketing";

      // Marketing tab = studio first; chat stays secondary.
      panel.hidden = false;
      panelTitle.textContent = meta.title;
      panelCopy.textContent = meta.text;
      status.textContent = "";
      reply.hidden = true;
      reply.textContent = "";

      researchPanel.hidden = !showResearch;
      if (showResearch) {
        loadResearch();
      }
      if (!showResearch) {
        message.focus();
      }
    });
  });

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (!activeIntent) {
      status.textContent = "Сначала выбери домен.";
      return;
    }

    const text = message.value.trim();
    if (!text) return;

    const submit = form.querySelector(".cta");
    submit.disabled = true;
    status.textContent = "Отправляю…";
    reply.hidden = true;

    try {
      const response = await fetch("/api/miniapp/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          conversationId,
          userId: resolveUserId() || "mini-anonymous",
          text,
          intent: activeIntent
        })
      });

      if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error(err.detail || err.title || `HTTP ${response.status}`);
      }

      const data = await response.json();
      reply.hidden = false;
      reply.textContent = data.text || "(пустой ответ)";
      status.textContent = `provider=${data.provider || "stub"}`;
    } catch (error) {
      status.textContent = error.message || "Ошибка запроса";
    } finally {
      submit.disabled = false;
    }
  });

  researchForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const userId = resolveUserId();
    if (!userId) {
      researchStatus.textContent = "Нужен Telegram userId (не аноним).";
      return;
    }
    if (!tg?.initData) {
      researchStatus.textContent = "Нужен Telegram initData (открой Mini App из Telegram).";
      return;
    }

    const handle = researchHandle.value.trim();
    if (/access_token|IGQVJ|EAA|sk-/i.test(handle)) {
      researchStatus.textContent = "Не вставляй IG token — только @handle.";
      return;
    }

    researchStatus.textContent = "Сохраняю…";
    researchForm.querySelector("#research-save").disabled = true;
    try {
      const response = await fetch("/api/miniapp/research/settings", {
        method: "PUT",
        headers: initDataHeaders(),
        body: JSON.stringify({
          userId,
          enabled: researchEnabled.checked,
          instagramHandle: handle,
          cadenceDays: Number(researchCadence.value) || 14,
          timezone: researchTz.value.trim() || null,
          notifyChatId: resolveNotifyChatId()
        })
      });
      if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error(err.detail || err.title || `HTTP ${response.status}`);
      }
      const saved = await response.json();
      paintSettings(saved);
      researchStatus.textContent = "Сохранено.";
    } catch (error) {
      researchStatus.textContent = error.message || "Ошибка сохранения";
    } finally {
      researchForm.querySelector("#research-save").disabled = false;
    }
  });

  researchRunBtn.addEventListener("click", async () => {
    const userId = resolveUserId();
    if (!userId) {
      researchStatus.textContent = "Нужен Telegram userId (не аноним).";
      return;
    }
    if (!tg?.initData) {
      researchStatus.textContent = "Нужен Telegram initData (открой Mini App из Telegram).";
      return;
    }

    researchStatus.textContent = "Запускаю research…";
    researchRunBtn.disabled = true;
    try {
      const response = await fetch("/api/miniapp/research/run", {
        method: "POST",
        headers: initDataHeaders(),
        body: JSON.stringify({
          userId,
          notifyChatId: resolveNotifyChatId()
        })
      });
      if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error(err.detail || err.title || `HTTP ${response.status}`);
      }
      const data = await response.json();
      if (data.settings) paintSettings(data.settings);
      researchStatus.textContent = data.message || data.outcome || "done";
      await loadResearch();
    } catch (error) {
      researchStatus.textContent = error.message || "Ошибка run";
    } finally {
      researchRunBtn.disabled = false;
    }
  });
})();
