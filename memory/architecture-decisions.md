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
- Decision: Phase 1 поднимается только через `docker compose up`. Секреты — env / Docker secrets, не файлы в git. K8s/Nginx — не в этой фазе.
- Consequences: Один compose на gateway + assistant-api.
- Alternatives considered: Локальный `dotnet run` без Docker; сразу Kubernetes.
- Security impact: `.env` в `.gitignore`. Пример только `.env.example` без реальных значений.
- Links: `memory/phase-plan.md`

