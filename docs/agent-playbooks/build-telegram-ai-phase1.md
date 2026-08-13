# Build Telegram AI Phase 1 Playbook

## Services

### telegram-gateway

- Принимает Telegram updates (webhook предпочтительнее long polling в prod).
- Мапит `telegramUserId` -> internal user id.
- Шлёт в `assistant-api` только `conversationId`, `userId`, `text`, `traceId`.
- Не пересылает bot token, Cursor key, raw update целиком без нужды.
- Отвечает в Telegram chat.

### assistant-api

- `POST /v1/chat`
- `GET /health/live`, `GET /health/ready`
- `ILlmProvider.CompleteAsync(ChatRequest, ct)`
- Phase 1 provider: `StubLlmProvider` (echo / canned reply)
- Versioned DTO, ProblemDetails
- Готовые extension points: `IToolProvider`, `IRagRetriever`, `IFileStore` — интерфейсы можно объявить, реализации Phase 2+ не писать

## Security

- `Telegram:BotToken` только в gateway UserSecrets/K8s Secret.
- `Cursor:ApiKey` только в assistant-api, и только когда появится provider. Phase 1 ключ не обязателен.
- Inter-service: `X-Service-Key` или HMAC, TLS.
- Если клиентский Cursor token когда-нибудь сохраняем: ASP.NET Data Protection / AES-GCM, master key не в git, plaintext только в memory на время вызова.
- Telegram message никогда не является каналом доставки API keys.

## Do not build in Phase 1

- Elasticsearch, embeddings, RAG pipeline
- MinIO files/video
- Cursor SDK runtime
- Multi-agent harness внутри assistant
