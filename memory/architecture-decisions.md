# Architecture Decisions

ADR-журнал для решений, которые должны пережить текущий чат.

## Template

```markdown
## ADR-000: <title>

- Status: proposed | accepted | superseded
- Date:
- Context:
- Decision:
- Consequences:
- Alternatives considered:
- Security impact:
- Links:
```

## Current Decisions

## ADR-001: Phase 1 = two services, stub LLM

- Status: accepted
- Date: 2026-08-13
- Context: Нужен Telegram bot + AI assistant. RAG/ES/files/Cursor SDK будут позже. API должно расширяться.
- Decision: Сейчас только `telegram-gateway` и `assistant-api`. LLM через `ILlmProvider`, default stub. Cursor SDK — Phase 2 provider, не отдельный публичный сервис на старте.
- Consequences: Быстрый вертикальный срез. Расширение аддитивными полями `/v1/chat` и новыми providers.
- Alternatives considered: Modular monolith один процесс; сразу multi-agent harness; сразу RAG+ES.
- Security impact: Меньше секретов и attack surface. Bot token и Cursor key разведены по сервисам.
- Links: `memory/current-project.md`

## ADR-002: Secrets never travel through Telegram

- Status: accepted
- Date: 2026-08-13
- Context: Клиентская подписка Cursor. Нужна безопасная работа с токенами.
- Decision: Telegram message не является каналом для API keys. Bot token только в gateway. Cursor API key только в assistant-api / future harness, из secret store. Если ключ надо хранить — envelope encryption (Data Protection / AES-GCM), master key вне git. Transit = TLS.
- Consequences: Пользователь бота не присылает ключ в чат. Ротация через secret provider.
- Alternatives considered: Пользователь шлёт ключ боту; ключ в query string; один shared secret на все сервисы.
- Security impact: Убирает утечку ключей в Telegram history/logs.
- Links: `memory/security-baseline.md`

## ADR-003: Telegram Mini App is the UI

- Status: accepted
- Date: 2026-08-13
- Context: Клиентская часть — Telegram. Нужен красивый интерфейс, не только текстовый echo.
- Decision: UI = Telegram bot + Mini App. Экраны Phase 1: салон, маркетинг, задачи. Кнопки вызывают `POST /v1/chat` с `intent`. Бизнес-логика доменов не реализуется в Phase 1.
- Consequences: Gateway отдаёт WebApp. Дизайн на стороне агента реализации.
- Alternatives considered: Только reply-клавиатура; отдельный web frontend вне Telegram.
- Security impact: Mini App не содержит bot token / service key. Init data проверять, когда появится auth пользователя.
- Links: `memory/current-project.md`

## ADR-004: Docker Compose is the Phase 1 runtime

- Status: accepted
- Date: 2026-08-13
- Context: Клиент должен поднимать сервисы просто.
- Decision: Phase 1 поднимается только через `docker compose up`. Секреты — env / Docker secrets, не файлы в git. K8s/Nginx — не в этой фазе. Helper `scripts/compose-up.sh` для nested Docker (ip_forward).
- Consequences: Один compose на gateway + assistant-api.
- Alternatives considered: Локальный `dotnet run` без Docker; сразу Kubernetes.
- Security impact: `.env` в `.gitignore`. Пример только `.env.example` без реальных значений.
- Links: `memory/phase-plan.md`

## ADR-005: Inter-service auth via shared service key header

- Status: accepted
- Date: 2026-08-13
- Context: gateway вызывает assistant-api; bot token не должен покидать gateway.
- Decision: `X-Service-Key` / `Assistant__ServiceKey` на всех non-health routes assistant-api. Mini App ходит в gateway proxy, не напрямую с ключом.
- Consequences: Один shared secret Phase 1; позже можно mTLS/JWT между сервисами.
- Alternatives considered: mTLS сразу; публичный chat без auth.
- Security impact: bot token и service key разделены; health остаётся без auth для probes.
- Links: `memory/integration-contracts.md`

## ADR-006: Phase 2 Cursor SDK via internal bridge + stub fallback

- Status: accepted
- Date: 2026-08-15
- Context: Нужен живой LLM через клиентский Cursor API key. `@cursor/sdk` — Node/TS. assistant-api — .NET. Без ключа compose должен оставаться зелёным.
- Decision: `CursorSdkLlmProvider` + `DomainHarness` (classify→agent→verify) в assistant-api. Runtime SDK — internal `cursor-sdk-bridge` (`@cursor/sdk`, cloud no-repo). API key encrypt-at-rest AES-GCM. `FallbackLlmProvider` → stub без ключа / при ошибке. Resume через optional `agentId`.
- Consequences: Один compose stack из трёх контейнеров; публичный контракт `/v1/chat` аддитивно расширен `agentId`. RAG/ES/MinIO не трогаем.
- Alternatives considered: Cloud Agents REST напрямую из C#; полный Connect protobuf adapter; отдельный публичный harness-сервис.
- Security impact: ключ не из Telegram; plaintext scrub после seal; bridge без public ports; verify режет secret-like output.
- Links: `memory/phase-plan.md`, `memory/security-baseline.md`

## ADR-007: Domain agent packs, not prompt-switch; not a service per domain

- Status: accepted
- Date: 2026-08-15
- Context: Phase 2 `DomainHarness` — один `@cursor/sdk` agent на диалог, домены = префиксы промпта, verify soft-skip. Это не масштабируется: разные MCP/skills/AGENTS.md/модели нельзя изолировать. Нужны specialist-агенты под салон / маркетинг / задачи.
- Decision: Phase 3 вводит **domain agent packs** на диске (`src/AgentPacks/<domain>/`: `AGENTS.md`, `skills/`, `prompts/`, `mcp.json`, `pack.json`). Router — отдельный pack `_router`. Runtime: отдельный `Agent.create`/resume **на домен**; affinity `conversationId+domain → agentId` в assistant-api. Packs остаются в modular monolith (`assistant-api` + `cursor-sdk-bridge`). Отдельный микросервис на домен — только когда появится независимый data ownership / deploy cadence. RAG/MinIO/Директ подключаются позже **в pack**, не новым shared prompt.
- Consequences: Phase 2 prompt-harness — переходный. `/v1/chat` аддитивно (resolved domain). Bridge контракт расширяется pack runtime. Cross-domain resume запрещён.
- Alternatives considered: оставить persona-switch; отдельный `salon-api`/`marketing-api` сразу; один mega-agent со всеми MCP.
- Security impact: MCP allowlist per pack; секреты MCP не в git; изоляция skills/MCP между доменами; verify обязан резать secret-leak и domain drift.
- Links: `memory/phase-plan.md` Phase 3, `memory/integration-contracts.md`

## ADR-008: Harness memory = profile + domain episodes, not SDK resume and not RAG

- Status: accepted
- Date: 2026-08-15
- Context: Cursor `agentId` помнит только текущий SDK-агент; новый create / смена домена / рестарт контейнера — контекст пользователя пропадает. Нужна короткая память «кто это» и «что уже сделали». Полный транскрипт и Elasticsearch — слишком жирно и смешивает домены.
- Decision: `assistant-api` владеет store (PostgreSQL, свои таблицы). Два слоя: (1) **UserProfile** — короткие shared факты; (2) **HarnessEpisode** `{userId, domain, task, result, at}` — бриф, cap last K. Specialist pack получает profile + свои episodes. Router — profile + last-domain, без чужих эпизодов. Запись эпизода после успешного verify; fail записи не валит HTTP-ответ. Это не RAG (Phase 4) и не `Agent.resume`.
- Consequences: specialist не слепой после нового `Agent.create`. Нужна БД у assistant-api. Токен-бюджет: жёсткий лимит символов на инжект.
- Alternatives considered: тащить всю историю в Cursor agent; один shared log на все домены; отдельный memory-microservice; сразу Elasticsearch.
- Security impact: domain isolation эпизодов; scanner на task/result; PII не в логах; retention/TTL позже явно.
- Links: `memory/phase-plan.md` Phase 3 slice `phase3-harness-memory`

