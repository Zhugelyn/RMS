(() => {
  const tg = window.Telegram?.WebApp;
  tg?.ready();
  tg?.expand();

  const copy = {
    salon: {
      title: "Салон",
      text: "Записи, расписание, клиенты — пока только shell с intent=salon."
    },
    marketing: {
      title: "Маркетинг",
      text: "Реклама и мониторинг — shell с intent=marketing."
    },
    tasks: {
      title: "Задачи",
      text: "Планирование и встречи — shell с intent=tasks."
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

  let activeIntent = null;
  let conversationId = `mini-${crypto.randomUUID()}`;

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
          userId: tg?.initDataUnsafe?.user?.id
            ? `tg-${tg.initDataUnsafe.user.id}`
            : "mini-anonymous",
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
})();
