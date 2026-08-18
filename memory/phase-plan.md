# Phase Plan

## Phase 1 — Shell

Status: **closed** (2026-08-13). Re-verified 2026-08-15: `dotnet test` 12/12. Нет незакрытых checkbox.

Acceptance:

- [x] `docker compose up` поднимает `telegram-gateway` и `assistant-api`.
- [x] `assistant-api`: `POST /v1/chat`, `GET /health/live`, `GET /health/ready`.
- [x] `ILlmProvider` интерфейс; default = stub, без внешнего LLM.
- [x] Telegram chat: update -> assistant-api -> ответ.
- [x] Mini App: экраны салон / маркетинг / задачи; кнопки шлют `/v1/chat` с `intent`; без реальной бизнес-логики.
- [x] Secrets из env/Docker secrets, не в git, не в логах, не в OpenAPI examples.
- [x] Inter-service auth есть; Telegram token не уходит в assistant-api.
- [x] Cursor API key не принимается из Telegram message.
- [x] `dotnet test` проходит.
- [x] ADR, catalog, contracts, run-log обновлены в `memory/`.

## Phase 2 — Cursor SDK harness

Acceptance (slice=`phase2-cursor-sdk`):

- [x] `CursorSdkLlmProvider` реализует `ILlmProvider` через internal `cursor-sdk-bridge` (`@cursor/sdk`).
- [x] Cursor API key в secret store, AES-GCM encrypt-at-rest; не в logs/git/response.
- [x] intent `salon|marketing|tasks` → harness classify → specialist agent → verify.
- [x] Resume по optional `agentId` (request/response).
- [x] Stub остаётся fallback, если ключа нет или SDK падает.
- [x] `dotnet test` проходит.
- [x] `docker compose up` поднимает stack без Cursor key (stub path).
- [x] `memory/phase-plan` + `run-log` обновлены.

Notes:

- `@cursor/sdk` / `cursor-sdk` как runtime LLM.
- Encrypted storage клиентского Cursor API key (`Cursor:MasterKey` + seal at startup).
- Phase 2 «specialist» = persona-switch (один Agent.create, разные промпт-префиксы). Это **не** domain agent packs.
- Оптимизация токенов: короткий specialist prompt, repo memory, не тащить RAG.

## Phase 3 — Domain agent packs (specialist harness)

Суть: под каждый домен — **свой** runtime-агент и pack, не общий Cursor agent с подменой 3 строк промпта.

Pack (source of truth на диске, owner = assistant-api):

```
src/AgentPacks/<domain>/
  AGENTS.md              # роль, границы, non-goals, failure mode
  skills/                # Cursor skills этого домена
  prompts/               # system / classify-hints / verify
  mcp.json               # allowlist MCP servers (без secrets)
  pack.json              # model, effort, runtime, resume policy
```

Домены на старте фазы: `salon` | `marketing` | `tasks` | `_router`.
`_router` — отдельный pack: только classify/route, **не** отвечает пользователю.

Service boundary:

- Не плодить `salon-api` / `marketing-api` как отдельные деплои, пока нет независимого ownership данных и cadence. Packs живут в modular monolith (`assistant-api` + `cursor-sdk-bridge`).
- `assistant-api` владеет: routing, `conversationId+domain → agentId`, **harness memory** (профиль + эпизоды), verify orchestration, secret injection в MCP.
- `cursor-sdk-bridge` исполняет pack: cwd/skills/MCP/model, `Agent.create` / `resume` **per domain**. Не владеет user memory.
- Telegram/Mini App по-прежнему шлют `intent`; не знают pack layout и не хранят память.
- RAG/MinIO/Директ **не** реализуются здесь. В pack можно зарезервировать MCP-слоты (stub/deny), реализации — Phase 5–7.

Harness memory (не Cursor `agentId` и не RAG):

- **Profile** (`userId`, shared): короткие факты о человеке (имя/как обращаться, TZ, язык, устойчивые предпочтения). Виден всем packs.
- **Episode** (`userId` + `domain`): «задача → результат» в одну-две строки. Salon-эпизоды не инжектятся в marketing pack (и наоборот).
- Это **не** полный транскрипт и не resume SDK. Новый `Agent.create` всё равно получает сжатый контекст из store.
- Инжект в specialist: profile ≤ N символов + last K эпизодов домена. Router видит profile + last-domain hint, не чужие эпизоды.
- Store: PostgreSQL **assistant-api** (свои таблицы). Не Elasticsearch, не отдельный memory-сервис.
- После успешного verify — записать эпизод. Падение записи не должно ронять ответ пользователю (log + retry later).
- PII: не логировать сырой memory dump; секреты в эпизоды не класть (тот же scanner).

Правила runtime:

- Смена домена ≠ resume чужого `agentId`. Salon-агент не продолжает marketing-тред.
- MCP/skills домена A недоступны агенту домена B.
- Verify жёсткий: drift по домену / secret-leak / пустой ответ → repair тем же pack или fail (не тихий skip как в Phase 2).
- Secrets MCP только из env/secret store, не из `mcp.json` и не из чата.

Acceptance (slice=`phase3-domain-agent-packs`):

- [x] Каталог `src/AgentPacks/{salon,marketing,tasks,_router}` с `AGENTS.md`, `skills/`, `prompts/`, `mcp.json`, `pack.json`. (slice `phase3-pack-layout`)
- [x] Router-pack классифицирует в домен (SDK path); fallback — pack classify-hints scoring; пользовательский ответ даёт только specialist pack.
- [x] `Agent.create` / resume **per domain**; mapping `conversationId+domain → agentId` (in-memory affinity).
- [x] Bridge принимает pack runtime (`packId` → local cwd/skills, empty MCP allowlist/model), не один голый `prompt`.
- [x] Cross-domain: marketing MCP/skills не грузятся в salon agent (отдельный cwd + тест изоляции).
- [x] Verify per-pack реально режет drift/secrets; Phase 2 soft-skip убран на Cursor path.
- [x] Harness memory: profile (shared) + episodes (per domain); инжект в pack; эпизод пишется после verify. (in-process store; Postgres durable — follow-up / Phase 4 settings)
- [x] Isolation memory: marketing pack не видит salon episodes (тест).
- [x] `/v1/chat` аддитивно: optional `domainPack` в response; `schemaVersion` не ломаем.
- [x] Без Cursor key — stub fallback как в Phase 2.
- [x] `dotnet test` (+ pack isolation + memory isolation tests) проходит; compose build context включает packs.
- [x] ADR/catalog/contracts/run-log обновлены.

Slices (один run = один):

1. [x] `phase3-pack-layout` — каталог packs + `pack.json` schema + `PackCatalog` loader/validator, без смены runtime chat/bridge/DomainHarness.
2. [x] `phase3-bridge-pack-runtime` — bridge: cwd/skills/MCP allowlist/model per pack.
3. [x] `phase3-router-and-affinity` — router pack + agentId affinity per domain.
4. [x] `phase3-verify-isolation` — жёсткий verify + isolation tests; выкинуть Phase 2 soft-skip.
5. [x] `phase3-harness-memory` — profile + episode store (in-memory contract), inject, write-after-verify, domain isolation.

Domain notes (product):

- `salon` = салон красоты **Babor**, Брянск; growth/идеи/локальный маркетинг **ради салона** + память фактов.
- `marketing` = рынок красоты, бренды, тренды, таргет/аудитории.
- `tasks` = расписание работ и напоминания.

Status Phase 3: **acceptance closed functionally** (2026-08-15). Leftover Postgres durable harness memory (ADR-008) closed in Phase 4 `phase4-postgres-settings` (2026-08-17). Packs runtime остаётся.

Non-goals Phase 3: RAG/ES, MinIO, Яндекс Директ, отдельный публичный harness-сервис, Kubernetes, полный chat log как память.

## Phase 4 — Marketing Instagram Research

Status: **closed** (2026-08-18). Hardening ✅; Phase 5 = Research Client UI (не Knowledge/RAG).

Суть: маркетинговый research по ленте **своего** Instagram-аккаунта. Источник — только бесплатный **Instagram Graph API**. Счедулер **14 дней**. Настройки в Mini App и команда бота `/research`. Картинки — **Cursor GenerateImage** через local `marketing` pack + volume (не отдельный OpenAI Images). Артефакты research (`snapshot` + `plan` + `episodes`) в **Postgres assistant-api** — это **не RAG**.

Service boundary:

- Owner данных research / settings / artifacts = `assistant-api` (Postgres).
- Graph API client — adapter внутри assistant-api (или pack MCP позже); secrets только env/secret store.
- `telegram-gateway`: команда `/research` + Mini App settings UI; не хранит IG token.
- `marketing` pack: GenerateImage skill/tool + volume для артефактов картинок; Apify/OpenAI Images запрещены.
- Не отдельный `instagram-research-api`, пока нет независимого ownership/deploy cadence.

Acceptance (фаза целиком; закрывать по slices):

- [x] Postgres: durable harness memory + research settings schema (snapshots/plans tables stub; fetch/inject later). Artifacts wiring incomplete until later slices.
- [x] Instagram Graph API своего аккаунта как единственный источник ленты; Apify нет.
- [x] Research artifacts ≠ RAG (нет embeddings/ES в этой фазе). Snapshot+plan persist + marketing inject ✅ (`phase4-research-artifacts`).
- [x] Счедулер research на 14 дней ✅ (`phase4-scheduler`); настройки в Mini App и `/research` в боте ✅ (`phase4-miniapp-research`).
- [x] GenerateImage через local marketing-pack + volume; не OpenAI Images API.
- [x] Secrets: IG token не из чата; encrypt-at-rest / env; не в logs/git.
- [x] `dotnet test` + compose зелёные; ADR-009/010/011, catalog, contracts, security, run-log.

Slices (один run = один):

1. [x] `phase4-docs` — README + phase-plan Phase 4 + сдвиг RAG на 5; ADR-009/010/011; catalog/contracts/security/run-log; `.env.example`. Без кода сервисов.
2. [x] `phase4-postgres-settings` — Postgres в compose; migrations; durable profile/episodes + research settings schema.
3. [x] `phase4-ig-graph` — Instagram Graph API client (свой аккаунт); token store; fetch media/insights; без Apify.
4. [x] `phase4-research-artifacts` — snapshot + plan + episodes persist; 14-дневный plan model; inject в marketing pack.
5. [x] `phase4-scheduler` — scheduler/job на окно 14 дней; idempotent runs; failure modes.
6. [x] `phase4-miniapp-research` — Mini App research settings + bot `/research` (start/status).
7. [x] `phase4-generate-image` — Cursor GenerateImage via local marketing-pack + volume mount.
8. [x] `phase4-hardening` — security/tests/limits; initData HMAC; token rotation notes; non-goals guard (no RAG/Apify/OpenAI Images).

Non-goals Phase 4:

- RAG / embeddings / Elasticsearch (→ Phase 7; Phase 5 = Research UI; Phase 6 = VK)
- Apify, scrapers чужих аккаунтов, платные crawl
- Отдельный OpenAI Images / DALL·E
- MinIO как object store (→ Phase 8), Яндекс Директ (→ Phase 9)
- Closing Phase 3 packs целиком «задним числом» — packs остаются; Postgres leftover закрывается здесь

## Phase 5 — Research Client UI

Status: **open leftover** — slice `phase5-research-ui` ✅; `phase5-ui-hardening` **deferred** (Phase 6 VK opened 2026-08-18).

Суть: клиентский UI поверх уже существующего research API. **Не RAG.** UI = Telegram Mini App + bot `web_app` (ADR-012), не отдельный сайт.

Acceptance (slice=`phase5-research-ui`):

- [x] `GET /v1/research/latest` (+ gateway proxy) отдаёт additive: `settings`, `snapshotSummary`, `analytics`, `posts[]`, `items[]` (+ `planPreview` сохранён); `schemaVersion` не ломаем.
- [x] Mini App вкладка Маркетинг = Research Studio (шапка / аналитика / галерея 14 дней / компактные настройки); не `pre`-dump.
- [x] Media proxy `GET /api/miniapp/research/media` — initData HMAC + `ResearchPhotoPathGuard`; `imageUrl` = proxy, не volume; IG token не в клиенте/URL.
- [x] Bot: `TELEGRAM__WEBAPPURL` → InlineKeyboard `web_app` «Открыть студию» на `/start` и `/research`; `setChatMenuButton` MenuButtonWebApp если URL задан.
- [x] `/research plan` — короткое резюме + ≤3 фото + кнопка Mini App (не 14 sendPhoto).
- [x] `dotnet test` зелёный; no RAG/Apify/OpenAI Images/MinIO.
- [x] ADR-012, catalog, contracts, README, run-log.

Slices:

1. [x] `phase5-research-ui` — studio Mini App + latest DTO + media proxy + bot web_app.
2. [ ] `phase5-ui-hardening` — polish/a11y/limits/edge cases studio. **Deferred** until Phase 6 closes or explicit request.

Non-goals Phase 5 UI:

- RAG / embeddings / Elasticsearch (→ Phase 7)
- Apify / OpenAI Images / MinIO
- Отдельный marketing website вне Telegram
- VK API (→ Phase 6)

## Phase 6 — Marketing VK Public Research

Status: **open** — `phase6-vk-docs` ✅; `phase6-vk-client` ✅; `phase6-vk-artifacts` ✅; `phase6-vk-settings` ✅; `phase6-vk-media` ✅; next=`phase6-vk-hardening`.

Суть: маркетинговый research по **открытым пабликам VK** (посты: текст + картинки вложений). Источник — только официальный **VK API** (`wall.get` / `utils.resolveScreenName`). Сервисный ключ приложения (`VK__SERVICETOKEN`). HTML-скрейп, Apify, неофициальный mobile API — запрещены.

Отличие от ADR-009 (IG): Graph чужие аккаунты не отдаёт → IG = только свой. VK официально отдаёт открытую стену чужого паблика. Это **не** scrape: allowlist screen_name/group id в settings, cap как у IG.

Service boundary:

- Owner данных research / settings / artifacts = `assistant-api` (Postgres). Additive `source=vk` в snapshot; `schemaVersion` не ломаем.
- VK API client — adapter внутри assistant-api (рядом с `HttpInstagramGraphClient`). Не отдельный `vk-research-api`.
- Secrets: service token только env/secret store / AES-GCM; не из чата / Mini App / query.
- Картинки постов: скачать CDN `*.userapi.com` в существующий volume + media proxy (MinIO → Phase 8).
- `telegram-gateway`: settings UI + `/research` аддитивно (не ломать IG-команды).
- `marketing` pack: inject snapshot как для IG. User VK ID OAuth (1h token) — не в этой фазе, пока service token достаточен.

Acceptance (фаза целиком; закрывать по slices):

- [x] Официальный VK API открытых пабликов; Apify/HTML нет. (`phase6-vk-client`: `IVkWallClient` / `HttpVkWallClient`)
- [x] Посты: `text` + photo attachments; closed/Donut → soft skip. (mapper ✅; `IVkResearchCapture` → snapshot)
- [x] Token не из чата; encrypt-at-rest / env. (`EncryptedVkTokenStore` + SecretScanner)
- [x] Snapshot+plan+episodes ≠ RAG; `source=vk` additive. (`phase6-vk-artifacts`)
- [x] ADR-013 + catalog/contracts/security/README/.env.example (docs slice).
- [x] Settings allowlist screen_name/owner_id; Mini App + `/research vk` аддитивно; token не из UI. (`phase6-vk-settings`)
- [x] CDN photos → volume + media proxy; URL CDN не долгоживущие. (`phase6-vk-media`)
- [x] `dotnet test` + compose зелёные (media slice; hardening later).

Slices (один run = один):

1. [x] `phase6-vk-docs` — ADR-013 + README Phase 6 + catalog/contracts/security/.env.example (`VK__SERVICETOKEN=`). Без кода сервисов.
2. [x] `phase6-vk-client` — `IVkWallClient` / `HttpVkWallClient`; service token store; `resolveScreenName` + `wall.get`; stub без токена; SSRF allowlist CDN.
3. [x] `phase6-vk-artifacts` — map VK items → snapshot (`source=vk`); inject marketing; cap ≤50; soft-fail.
4. [x] `phase6-vk-settings` — settings: allowlist пабликов (screen_name / owner_id); Mini App + `/research` аддитивно; не принимать token из UI.
5. [x] `phase6-vk-media` — download photo sizes в volume; media proxy; URL CDN не хранить как долгоживущие.
6. [ ] `phase6-vk-hardening` — caps, tests, token rotation notes, non-goals guard (no scrape/Apify/user-OAuth/RAG/MinIO/Direct).

Non-goals Phase 6:

- RAG / embeddings / Elasticsearch (→ Phase 7)
- MinIO (→ Phase 8), Яндекс Директ (→ Phase 9)
- Apify / HTML scrape / `m.vk.com`
- VK ID user OAuth / community token чужих пабликов
- Комментарии и профили авторов (PII / 152-ФЗ)
- Закрытые группы, Donut-only, stories чужих
- Отдельный `vk-research-api`

## Phase 7 — Knowledge (RAG)

- RAG service + embeddings + Elasticsearch. Можно готовые фреймворки.
- Это **документы/база знаний**, не harness memory (Phase 3) и не research snapshot/plan (Phase 4) и не research UI (Phase 5) и не VK wall snapshot (Phase 6).
- Отдельный ownership данных и retriever contract.
- Не смешивать индекс салона и маркетинга без явного решения.
- Retriever подключается **в pack** домена (MCP/skill), не в общий промпт.

## Phase 8 — Files / Video / Images storage

- MinIO, metadata DB, scanning hook, presigned URLs.
- Работа с фото/видео через отдельные adapters (поверх GenerateImage из Phase 4 / VK downloads из Phase 6 при необходимости).
- Большие файлы не проксировать через assistant-api без причины.
- File tools — MCP/skill конкретного pack, не shared agent.

## Phase 9 — External tools

- Яндекс Директ и другие ads/CRM integrations.
- Отдельные tool adapters, секреты per-integration, least privilege.
- Marketing Direct = MCP только `AgentPacks/marketing`. Salon CRM — только `salon`. Не общий toolbox.
