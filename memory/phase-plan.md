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
- RAG/MinIO/Директ **не** реализуются здесь. В pack можно зарезервировать MCP-слоты (stub/deny), реализации — Phase 4–6.

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
- [x] Harness memory: profile (shared) + episodes (per domain); инжект в pack; эпизод пишется после verify. (in-process store; Postgres durable — follow-up)
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

Status Phase 3: **acceptance closed functionally** (2026-08-15). Leftover harden: Postgres for harness memory (ADR-008).

Non-goals Phase 3: RAG/ES, MinIO, Яндекс Директ, отдельный публичный harness-сервис, Kubernetes, полный chat log как память.

## Phase 4 — Knowledge

- RAG service + embeddings + Elasticsearch. Можно готовые фреймворки.
- Это **документы/база знаний**, не harness memory из Phase 3 (профиль + «задача→результат»).
- Отдельный ownership данных и retriever contract.
- Не смешивать индекс салона и маркетинга без явного решения.
- Retriever подключается **в pack** домена (MCP/skill), не в общий промпт.

## Phase 5 — Files / Video / Images

- MinIO, metadata DB, scanning hook, presigned URLs.
- Генерация картинок и работа с фото/видео через отдельные adapters.
- Большие файлы не проксировать через assistant-api без причины.
- File tools — MCP/skill конкретного pack, не shared agent.

## Phase 6 — External tools

- Яндекс Директ и другие ads/CRM integrations.
- Отдельные tool adapters, секреты per-integration, least privilege.
- Marketing Direct = MCP только `AgentPacks/marketing`. Salon CRM — только `salon`. Не общий toolbox.
