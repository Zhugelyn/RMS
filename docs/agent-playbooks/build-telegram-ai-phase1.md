# Build Telegram AI Phase 1 Playbook

## Runtime

```powershell
docker compose up --build
```

Секреты только в `.env` (gitignored). В репо — `.env.example` без значений.

- `Telegram__BotToken`
- `Assistant__ServiceKey`

## Services

### telegram-gateway

- Telegram updates (webhook предпочтительнее long polling в prod).
- Mini App: экраны салон / маркетинг / задачи. UI должен быть аккуратным, не сырой HTML.
- Кнопки Mini App / меню шлют в assistant-api `intent`.
- В assistant-api уходит `conversationId`, `userId`, `text`, `traceId`, optional `intent`.
- Не пересылает bot token, Cursor key, raw update целиком без нужды.
- Отвечает в Telegram chat.

### assistant-api

- `POST /v1/chat`
- `GET /health/live`, `GET /health/ready`
- `ILlmProvider.CompleteAsync(ChatRequest, ct)`
- Phase 1 provider: `StubLlmProvider`
- Можно объявить `IToolProvider`, `IRagRetriever`, `IFileStore` — реализации Phase 2+ не писать
- ProblemDetails, inter-service auth

## Security

- `Telegram:BotToken` только в gateway.
- `Cursor:ApiKey` не нужен в Phase 1.
- Inter-service: `X-Service-Key` / `Assistant__ServiceKey`.
- Encrypt-at-rest заложить только как extension point, не внедрять KMS.
- Telegram message никогда не канал для API keys.
- Mini App не содержит секретов.

## Do not build in Phase 1

- Elasticsearch, embeddings, RAG frameworks
- MinIO, image generation, photo/video pipeline
- Cursor SDK runtime
- Яндекс Директ
- Multi-agent harness внутри assistant
- Kubernetes / Nginx
