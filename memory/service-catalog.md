# Service Catalog

Проект `telegram-ai`. Код сервисов ещё не создан; границы зафиксированы.

## Service: telegram-gateway

- Owner: telegram-bot-agent
- Business capability: приём Telegram updates и доставка ответов в чат
- Public API: Telegram webhook
- Events produced: нет в Phase 1
- Events consumed: нет
- Database: optional mapping telegramUserId -> userId (своя БД, если понадобится persist)
- Object storage: нет
- External dependencies: Telegram Bot API, assistant-api
- Security notes: владеет `Telegram:BotToken`; не хранит Cursor API key; не принимает секреты из сообщений

## Service: assistant-api

- Owner: ai-assistant-agent
- Business capability: chat completion shell для бота и будущих клиентов
- Public API: `POST /v1/chat`, health
- Events produced: нет в Phase 1
- Events consumed: нет
- Database: conversation/message store later; Phase 1 можно in-memory/stub
- Object storage: нет в Phase 1
- External dependencies: `ILlmProvider` (stub now; Cursor SDK later)
- Security notes: inter-service auth; будущий `Cursor:ApiKey` только здесь; encrypt-at-rest если ключ клиентский

## Reserved (do not implement now)

- rag-service
- embedding-service
- search-elasticsearch
- files-minio
- assistant-harness (Cursor SDK orchestrator)

## Template

```markdown
## Service: <name>

- Owner:
- Business capability:
- Public API:
- Events produced:
- Events consumed:
- Database:
- Object storage:
- External dependencies:
- SLO/SLA:
- Security notes:
```

