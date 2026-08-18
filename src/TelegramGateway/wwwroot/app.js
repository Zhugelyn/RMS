(() => {
  const tg = window.Telegram?.WebApp;
  tg?.ready();
  tg?.expand();

  // Studio UI limits (Phase 5 hardening) — keep in sync with server caps.
  const LIMITS = {
    galleryCap: 14,
    postsCap: 20,
    postsServerCap: 50,
    vkMax: 10,
    cadenceMin: 1,
    cadenceMax: 90,
    messageMax: 1000,
    captionPreview: 160
  };

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
  const researchVk = document.getElementById("research-vk");
  const researchTz = document.getElementById("research-tz");
  const researchCadence = document.getElementById("research-cadence");
  const researchLast = document.getElementById("research-last");
  const researchNext = document.getElementById("research-next");
  const researchErrorLine = document.getElementById("research-error-line");
  const researchStatus = document.getElementById("research-status");
  const researchRunBtn = document.getElementById("research-run");
  const researchVkRunBtn = document.getElementById("research-vk-run");
  const researchSaveBtn = document.getElementById("research-save");
  const studioHeader = document.getElementById("studio-header");
  const studioHandle = document.getElementById("studio-handle");
  const studioEnabled = document.getElementById("studio-enabled");
  const studioSourceRow = document.getElementById("studio-source-row");
  const studioSource = document.getElementById("studio-source");
  const studioSummary = document.getElementById("studio-summary");
  const analyticsGrid = document.getElementById("analytics-grid");
  const analyticsEmpty = document.getElementById("analytics-empty");
  const planGallery = document.getElementById("plan-gallery");
  const galleryEmpty = document.getElementById("gallery-empty");
  const postsFeed = document.getElementById("posts-feed");
  const postsEmpty = document.getElementById("posts-empty");
  const postsLimitHint = document.getElementById("posts-limit-hint");
  const vkCount = document.getElementById("vk-count");

  let activeIntent = null;
  let conversationId = `mini-${crypto.randomUUID()}`;
  const blobUrls = [];
  let lastVkCommunities = [];

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

  function hasTelegramAuth() {
    return !!(resolveUserId() && tg?.initData);
  }

  function setMutationEnabled(enabled) {
    const on = !!enabled;
    if (researchSaveBtn) researchSaveBtn.disabled = !on;
    if (researchRunBtn) researchRunBtn.disabled = !on;
    if (researchVkRunBtn) researchVkRunBtn.disabled = !on;
  }

  function fmt(iso) {
    if (!iso) return "—";
    try {
      return new Date(iso).toISOString().replace("T", " ").slice(0, 16) + "Z";
    } catch {
      return String(iso);
    }
  }

  function clampCadence(raw) {
    const n = Number(raw);
    if (!Number.isFinite(n)) return 14;
    return Math.min(LIMITS.cadenceMax, Math.max(LIMITS.cadenceMin, Math.trunc(n)));
  }

  function formatVkCommunities(list) {
    if (!Array.isArray(list) || list.length === 0) return "";
    return list.map((c) => {
      if (c.screenName) return c.screenName;
      if (c.ownerId != null) return String(c.ownerId);
      return "";
    }).filter(Boolean).join("\n");
  }

  function parseVkCommunities(text) {
    if (!text || !text.trim()) return [];
    const out = [];
    const seen = new Set();
    let truncated = false;
    for (const part of text.split(/[\s,;]+/).map((x) => x.trim()).filter(Boolean)) {
      let t = part.replace(/^@/, "");
      t = t.replace(/^https?:\/\/(m\.)?vk\.com\//i, "").replace(/\/$/, "");
      if (/access_token|service_token|VK__|RAG__|ELASTIC|IGQVJ|EAA|sk-/i.test(t)) {
        throw new Error("Не вставляй VK/IG token — только screen_name / owner_id.");
      }
      let item = null;
      const club = /^(?:club|public|event)(\d+)$/i.exec(t);
      if (club) {
        item = { ownerId: -Number(club[1]) };
      } else if (/^-?\d+$/.test(t) && Number(t) !== 0) {
        item = { ownerId: Number(t) };
      } else if (/^[A-Za-z0-9._]{2,64}$/.test(t)) {
        item = { screenName: t };
      } else {
        throw new Error(`Не распознал VK цель: ${part}`);
      }
      const key = item.screenName ? `n:${item.screenName.toLowerCase()}` : `o:${item.ownerId}`;
      if (seen.has(key)) continue;
      seen.add(key);
      if (out.length >= LIMITS.vkMax) {
        truncated = true;
        break;
      }
      out.push(item);
    }
    out._truncated = truncated;
    return out;
  }

  function updateVkCount(list) {
    if (!vkCount) return;
    const n = Array.isArray(list) ? list.length : 0;
    vkCount.textContent = `${n} / ${LIMITS.vkMax}`;
    vkCount.dataset.count = String(n);
    vkCount.classList.toggle("is-full", n >= LIMITS.vkMax);
  }

  function paintSettings(s) {
    researchEnabled.checked = !!s.enabled;
    researchHandle.value = s.instagramHandle ? `@${s.instagramHandle}` : "";
    lastVkCommunities = Array.isArray(s.vkCommunities) ? s.vkCommunities : [];
    researchVk.value = formatVkCommunities(lastVkCommunities);
    updateVkCount(lastVkCommunities);
    researchTz.value = s.timezone || "";
    researchCadence.value = String(clampCadence(s.cadenceDays || 14));
    researchLast.textContent = fmt(s.lastRunAt);
    researchNext.textContent = fmt(s.nextRunAt);
    const err = s.lastError || "—";
    researchErrorLine.textContent = "last error: " + err;
    researchErrorLine.classList.toggle("has-error", !!(s.lastError && s.lastError !== "—"));
    const vkN = lastVkCommunities.length;
    studioHandle.textContent = s.instagramHandle
      ? `@${s.instagramHandle}` + (vkN ? ` · vk:${vkN}` : "")
      : (vkN ? `vk:${vkN}` : "@—");
    studioEnabled.textContent = s.enabled ? "on" : "off";
    studioEnabled.dataset.on = s.enabled ? "1" : "0";
    studioEnabled.setAttribute("aria-label", s.enabled ? "Research включён" : "Research выключен");
  }

  function normalizeSource(source, summary) {
    const raw = (source || "").toLowerCase().trim();
    if (raw === "vk" || raw === "instagram") return raw;
    const s = String(summary || "").toLowerCase();
    if (/\bsource=vk\b/.test(s) || /\bvk\b/.test(s) && /posts=/.test(s)) return "vk";
    if (/\bsource=instagram\b/.test(s) || /graph/.test(s)) return "instagram";
    return raw || "";
  }

  function paintSource(latest) {
    const source = normalizeSource(latest?.source, latest?.snapshotSummary);
    if (!source) {
      studioSourceRow.hidden = true;
      studioSource.textContent = "—";
      studioSource.dataset.source = "";
    } else {
      studioSourceRow.hidden = false;
      studioSource.textContent = source;
      studioSource.dataset.source = source;
    }

    if (latest?.snapshotSummary) {
      studioSummary.hidden = false;
      studioSummary.textContent = String(latest.snapshotSummary).slice(0, 280);
    } else {
      studioSummary.hidden = true;
      studioSummary.textContent = "";
    }
  }

  function paintAnalytics(analytics, source) {
    analyticsGrid.innerHTML = "";
    const noInsights = !analytics || (
      analytics.impressions == null &&
      analytics.reach == null &&
      analytics.engagement == null &&
      analytics.saved == null
    );
    if (noInsights) {
      analyticsEmpty.hidden = false;
      analyticsEmpty.textContent = source === "vk"
        ? "VK snapshot без Graph insights — смотри посты ниже."
        : "нет данных Graph insights";
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
      el.setAttribute("role", "group");
      el.setAttribute("aria-label", label);
      el.innerHTML = `<span class="analytics-label">${label}</span><span class="analytics-value">${value ?? "—"}</span>`;
      analyticsGrid.appendChild(el);
    }
  }

  function paintPosts(posts, source) {
    postsFeed.innerHTML = "";
    const list = Array.isArray(posts) ? posts.slice(0, LIMITS.postsCap) : [];
    if (postsLimitHint) {
      postsLimitHint.textContent = `Показ ≤${LIMITS.postsCap} из ≤${LIMITS.postsServerCap}` +
        (source ? ` · source=${source}` : "");
    }
    if (list.length === 0) {
      postsEmpty.hidden = false;
      postsEmpty.textContent = source === "vk"
        ? "VK постов нет — проверь allowlist и Run VK."
        : "Постов нет — Run IG / Run VK или дождись scheduler.";
      return;
    }
    postsEmpty.hidden = true;
    for (const post of list) {
      const li = document.createElement("li");
      li.className = "post-card";
      if (source) li.dataset.source = source;
      const caption = String(post.caption || "").slice(0, LIMITS.captionPreview);
      const when = post.timestamp ? fmt(post.timestamp) : "—";
      const metrics = [
        post.impressions != null ? `imp ${post.impressions}` : null,
        post.reach != null ? `reach ${post.reach}` : null,
        post.engagement != null ? `eng ${post.engagement}` : null
      ].filter(Boolean).join(" · ");
      li.innerHTML = `
        <div class="post-meta">
          <time datetime="${escapeAttr(post.timestamp || "")}">${escapeHtml(when)}</time>
          ${source ? `<span class="post-source">${escapeHtml(source)}</span>` : ""}
        </div>
        <p class="post-caption">${escapeHtml(caption || "(без текста)")}</p>
        ${metrics ? `<p class="post-metrics">${escapeHtml(metrics)}</p>` : ""}`;
      postsFeed.appendChild(li);
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

  function mediaLooksVk(item) {
    const path = String(item.mediaPath || item.imageUrl || "");
    return /\/vk\//i.test(path) || /research-media\/[^/]+\/vk\//i.test(path);
  }

  async function paintGallery(items, source) {
    revokeBlobs();
    planGallery.innerHTML = "";
    const list = Array.isArray(items) ? items.slice(0, LIMITS.galleryCap) : [];
    if (list.length === 0) {
      galleryEmpty.hidden = false;
      return;
    }
    galleryEmpty.hidden = true;

    for (const item of list) {
      const card = document.createElement("article");
      card.className = "plan-card";
      card.dataset.status = item.status || "draft";
      const itemSource = mediaLooksVk(item) ? "vk" : (source || "");
      if (itemSource) card.dataset.source = itemSource;

      const media = document.createElement("div");
      media.className = "plan-media";
      media.setAttribute("aria-hidden", item.imageUrl ? "false" : "true");
      if (item.imageUrl) {
        const src = await loadMediaBlob(item.imageUrl);
        if (src) {
          const img = document.createElement("img");
          img.alt = `План ${item.date || ""}`.trim() || "plan";
          img.loading = "lazy";
          img.decoding = "async";
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
      const sourceChip = itemSource
        ? `<span class="plan-source" aria-label="источник ${escapeAttr(itemSource)}">${escapeHtml(itemSource)}</span>`
        : "";
      body.innerHTML = `
        <div class="plan-meta">
          <time>${escapeHtml(item.date || "—")}</time>
          <span class="plan-status">${escapeHtml(item.status || "draft")}</span>
          ${sourceChip}
        </div>
        <p class="plan-caption">${escapeHtml(item.caption || "")}</p>
        <p class="plan-tags">${escapeHtml(tags)}</p>
        <div class="plan-prompt">
          <code class="plan-prompt-text">${escapeHtml(item.imagePrompt || "")}</code>
          <button type="button" class="copy-btn" data-copy="${escapeAttr(item.imagePrompt || "")}" aria-label="Скопировать image prompt">copy</button>
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

  researchVk?.addEventListener("input", () => {
    try {
      const parsed = parseVkCommunities(researchVk.value);
      updateVkCount(parsed);
    } catch {
      updateVkCount([]);
    }
  });

  async function loadResearch() {
    const userId = resolveUserId();
    if (!userId) {
      researchStatus.textContent = "Открой Mini App из Telegram (нужен tg userId).";
      researchPanel.classList.add("is-error");
      setMutationEnabled(false);
      paintAnalytics(null, "");
      paintPosts([], "");
      await paintGallery([], "");
      paintSource(null);
      return;
    }

    setMutationEnabled(hasTelegramAuth());
    if (!tg?.initData) {
      researchStatus.textContent = "Просмотр без initData: Save / Run недоступны.";
    }

    researchStatus.textContent = researchStatus.textContent || "Загружаю…";
    researchPanel.classList.add("is-loading");
    researchPanel.setAttribute("aria-busy", "true");
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
        const source = normalizeSource(latest.source, latest.snapshotSummary);
        paintSource(latest);
        paintAnalytics(latest.analytics, source);
        paintPosts(latest.posts || [], source);
        await paintGallery(latest.items || [], source);
      } else {
        paintSource(null);
        paintAnalytics(null, "");
        paintPosts([], "");
        await paintGallery([], "");
      }
      if (tg?.initData) {
        researchStatus.textContent = "";
      }
      researchPanel.classList.remove("is-error");
    } catch (error) {
      researchStatus.textContent = error.message || "Ошибка загрузки";
      researchPanel.classList.add("is-error");
      paintSource(null);
      paintAnalytics(null, "");
      paintPosts([], "");
      await paintGallery([], "");
    } finally {
      researchPanel.classList.remove("is-loading");
      researchPanel.setAttribute("aria-busy", "false");
    }
  }

  buttons.forEach((btn) => {
    btn.addEventListener("click", () => {
      activeIntent = btn.dataset.intent;
      buttons.forEach((b) => {
        const on = b === btn;
        b.classList.toggle("is-active", on);
        b.setAttribute("aria-selected", on ? "true" : "false");
      });
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
        studioHeader?.focus?.({ preventScroll: false });
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
    if (text.length > LIMITS.messageMax) {
      status.textContent = `Сообщение > ${LIMITS.messageMax} символов.`;
      return;
    }

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
    if (/access_token|IGQVJ|EAA|sk-|VK__|RAG__|ELASTIC/i.test(handle)) {
      researchStatus.textContent = "Не вставляй IG/VK token — только @handle.";
      return;
    }

    let vkCommunities;
    try {
      vkCommunities = parseVkCommunities(researchVk.value);
    } catch (error) {
      researchStatus.textContent = error.message || "Ошибка VK allowlist";
      return;
    }

    const cadenceDays = clampCadence(researchCadence.value);
    researchCadence.value = String(cadenceDays);

    researchStatus.textContent = "Сохраняю…";
    researchSaveBtn.disabled = true;
    try {
      const response = await fetch("/api/miniapp/research/settings", {
        method: "PUT",
        headers: initDataHeaders(),
        body: JSON.stringify({
          userId,
          enabled: researchEnabled.checked,
          instagramHandle: handle,
          vkCommunities,
          cadenceDays,
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
      researchStatus.textContent = vkCommunities._truncated
        ? `Сохранено (VK allowlist обрезан до ${LIMITS.vkMax}).`
        : "Сохранено.";
    } catch (error) {
      researchStatus.textContent = error.message || "Ошибка сохранения";
    } finally {
      researchSaveBtn.disabled = !hasTelegramAuth();
    }
  });

  async function runResearch(source) {
    const userId = resolveUserId();
    if (!userId) {
      researchStatus.textContent = "Нужен Telegram userId (не аноним).";
      return;
    }
    if (!tg?.initData) {
      researchStatus.textContent = "Нужен Telegram initData (открой Mini App из Telegram).";
      return;
    }

    if (source === "vk") {
      let vkList = lastVkCommunities;
      try {
        vkList = parseVkCommunities(researchVk.value);
      } catch (error) {
        researchStatus.textContent = error.message || "Ошибка VK allowlist";
        return;
      }
      if (!vkList.length) {
        researchStatus.textContent = "VK allowlist пуст — добавь screen_name / owner_id и Save.";
        return;
      }
    }

    researchStatus.textContent = source === "vk" ? "Запускаю VK research…" : "Запускаю IG research…";
    researchRunBtn.disabled = true;
    if (researchVkRunBtn) researchVkRunBtn.disabled = true;
    try {
      const body = {
        userId,
        notifyChatId: resolveNotifyChatId()
      };
      if (source) body.source = source;
      const response = await fetch("/api/miniapp/research/run", {
        method: "POST",
        headers: initDataHeaders(),
        body: JSON.stringify(body)
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
      setMutationEnabled(hasTelegramAuth());
    }
  }

  researchRunBtn.addEventListener("click", () => runResearch(null));
  researchVkRunBtn?.addEventListener("click", () => runResearch("vk"));

  setMutationEnabled(hasTelegramAuth());
  updateVkCount([]);
})();
