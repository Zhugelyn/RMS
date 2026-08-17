(() => {
  const tg = window.Telegram?.WebApp;
  tg?.ready();
  tg?.expand();

  const copy = {
    salon: {
      title: "Салон",
      text: "Записи, расписание, клиенты — intent=salon."
    },
    marketing: {
      title: "Маркетинг",
      text: "Рынок и тренды — intent=marketing. Ниже — Instagram Research своего аккаунта."
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
  const researchError = document.getElementById("research-error");
  const researchPlan = document.getElementById("research-plan");
  const researchStatus = document.getElementById("research-status");
  const researchRunBtn = document.getElementById("research-run");

  let activeIntent = null;
  let conversationId = `mini-${crypto.randomUUID()}`;

  function resolveUserId() {
    const id = tg?.initDataUnsafe?.user?.id;
    return id ? `tg-${id}` : null;
  }

  function resolveNotifyChatId() {
    // Prefer private chat id = user id when opened from Telegram.
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
    researchError.textContent = s.lastError || "—";
  }

  async function loadResearch() {
    const userId = resolveUserId();
    if (!userId) {
      researchStatus.textContent = "Открой Mini App из Telegram (нужен tg userId).";
      return;
    }

    researchStatus.textContent = "Загружаю…";
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
        if (latest.planPreview) {
          researchPlan.hidden = false;
          researchPlan.textContent = latest.planPreview;
        } else {
          researchPlan.hidden = true;
          researchPlan.textContent = "";
        }
      }
      researchStatus.textContent = "";
    } catch (error) {
      researchStatus.textContent = error.message || "Ошибка загрузки";
    }
  }

  buttons.forEach((btn) => {
    btn.addEventListener("click", () => {
      activeIntent = btn.dataset.intent;
      buttons.forEach((b) => b.classList.toggle("is-active", b === btn));
      const meta = copy[activeIntent];
      panel.hidden = false;
      panelTitle.textContent = meta.title;
      panelCopy.textContent = meta.text;
      status.textContent = "";
      reply.hidden = true;
      reply.textContent = "";
      message.focus();

      const showResearch = activeIntent === "marketing";
      researchPanel.hidden = !showResearch;
      if (showResearch) {
        loadResearch();
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
        headers: { "Content-Type": "application/json" },
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

    researchStatus.textContent = "Запускаю research…";
    researchRunBtn.disabled = true;
    try {
      const response = await fetch("/api/miniapp/research/run", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
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
      if (data.planPreview) {
        researchPlan.hidden = false;
        researchPlan.textContent = data.planPreview;
      }
      researchStatus.textContent = data.message || data.outcome || "done";
    } catch (error) {
      researchStatus.textContent = error.message || "Ошибка run";
    } finally {
      researchRunBtn.disabled = false;
    }
  });
})();
