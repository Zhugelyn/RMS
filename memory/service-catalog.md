# Service Catalog

Проект `telegram-ai`. Phase 1 shell реализован.

## Service: telegram-gateway

- Owner: telegram-bot-agent
- Business capability: Telegram chat + Mini App UI; доставка запросов в assistant-api
- Code: `src/TelegramGateway`
- Public API: `POST /telegram/webhook`; `POST /api/miniapp/chat`; static Mini App (`/`); health
- Events produced: нет в Phase 1
- Events consumed: нет
- Database: optional mapping telegramUserId -> userId (пока inline `tg-{id}`)
- Object storage: нет
- External dependencies: Telegram Bot API, assistant-api
- Security notes: владеет `Telegram:BotToken`; не хранит Cursor API key; SecretScanner отклоняет секреты из сообщений; Mini App без секретов; HttpClient logging для Telegram отключён

## Service: assistant-api

- Owner: ai-assistant-agent
- Business capability: chat completion shell для бота, Mini App и будущих клиентов
- Code: `src/AssistantApi`
- Public API: `POST /v1/chat`, `GET /health/live`, `GET /health/ready`
- Events produced: нет в Phase 1
- Events consumed: нет
- Database: conversation store later; Phase 1 in-memory/stub
- Object storage: нет в Phase 1
- External dependencies: `ILlmProvider` = `StubLlmProvider`
- Security notes: inter-service auth `X-Service-Key` / `Assistant__ServiceKey`; rejects secret-like chat text; будущий `Cursor:ApiKey` только здесь

## Reserved (do not implement now)

- assistant-harness (Cursor SDK orchestrator, домены маркетинг/салон/быт)
- rag-service
- embedding-service
- search-elasticsearch
- files-minio
- yandex-direct-adapter
- media-generation-adapter
