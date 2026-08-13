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

