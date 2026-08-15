# Service Catalog

Проект `telegram-ai`. Phase 2 Cursor SDK harness реализован.

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
- Business capability: chat completion + Phase 2 Cursor SDK harness
- Code: `src/AssistantApi`
- Public API: `POST /v1/chat`, `GET /health/live`, `GET /health/ready`
- Events produced: нет
- Events consumed: нет
- Database: conversation store later; Phase 2 in-memory agentId passthrough
- Object storage: нет
- External dependencies: `ILlmProvider` = `FallbackLlmProvider` (`CursorSdkLlmProvider` → stub); internal `cursor-sdk-bridge`
- Security notes: inter-service auth `X-Service-Key`; rejects secret-like chat text; Cursor API key encrypt-at-rest (AES-GCM), never in logs/response/Telegram

## Service: cursor-sdk-bridge (internal)

- Owner: ai-assistant-agent
- Business capability: thin Node HTTP wrapper around `@cursor/sdk` (create/resume/send/wait)
- Code: `src/CursorSdkBridge`
- Public API: нет (compose-internal `:8090`)
- Auth: receives decrypted API key per-request from assistant-api over internal network; does not persist key
- Security notes: non-root; health only; no public ports

## Reserved (do not implement now)

- rag-service
- embedding-service
- search-elasticsearch
- files-minio
- yandex-direct-adapter
- media-generation-adapter
