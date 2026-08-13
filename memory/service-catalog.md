# Service Catalog

Проект `telegram-ai`. Код сервисов ещё не создан; границы зафиксированы.

## Service: telegram-gateway

- Owner: telegram-bot-agent
- Business capability: Telegram chat + Mini App UI; доставка запросов в assistant-api
- Public API: Telegram webhook; static Mini App
- Events produced: нет в Phase 1
- Events consumed: нет
- Database: optional mapping telegramUserId -> userId
- Object storage: нет
- External dependencies: Telegram Bot API, assistant-api
- Security notes: владеет `Telegram:BotToken`; не хранит Cursor API key; не принимает секреты из сообщений; Mini App не содержит секретов

## Service: assistant-api

- Owner: ai-assistant-agent
- Business capability: chat completion shell для бота, Mini App и будущих клиентов
- Public API: `POST /v1/chat`, health
- Events produced: нет в Phase 1
- Events consumed: нет
- Database: conversation store later; Phase 1 in-memory/stub
- Object storage: нет в Phase 1
- External dependencies: `ILlmProvider` (stub now; Cursor SDK later)
- Security notes: inter-service auth (`Assistant__ServiceKey`); будущий `Cursor:ApiKey` только здесь; encrypt-at-rest если ключ клиентский

## Reserved (do not implement now)

- assistant-harness (Cursor SDK orchestrator, домены маркетинг/салон/быт)
- rag-service
- embedding-service
- search-elasticsearch
- files-minio
- yandex-direct-adapter
- media-generation-adapter
