# Integration Contracts

Храни здесь устойчивые REST/gRPC/event/file contracts между сервисами.

## REST/gRPC Contract Template

```markdown
## API: <service>.<operation>

- Owner:
- Consumers:
- Method/path or RPC:
- Request:
- Response:
- Errors:
- Auth:
- Rate/size limits:
- Backward compatibility:
```

## Event Contract Template

```markdown
## Event: <name>

- Owner:
- Broker: RabbitMQ | Kafka
- Version:
- Schema:
- Producer:
- Consumers:
- Correlation/causation:
- Idempotency key:
- Retry/DLQ:
- Compatibility policy:
```

## API: assistant-api.chat

- Owner: assistant-api
- Consumers: telegram-gateway; later other clients
- Method/path: `POST /v1/chat`
- Request:
  - `schemaVersion: 1`
  - `conversationId`
  - `userId`
  - `text`
  - `traceId`
  - `intent` optional: `salon` | `marketing` | `tasks` | `general`
  - reserved optional later: `attachments[]`, `tools[]`, `retrieval`, `provider`
- Response:
  - `schemaVersion: 1`
  - `conversationId`
  - `messageId`
  - `text`
  - `provider`: `stub` | later `cursor-sdk`
- Errors: ProblemDetails
- Auth: `X-Service-Key` header = `Assistant__ServiceKey`
- Rate/size limits: text max 4000; later file size in files phase
- Backward compatibility: additive optional fields only
- Implemented: `src/AssistantApi` + gateway proxy `POST /api/miniapp/chat`

## API: telegram-gateway.webhook

- Owner: telegram-gateway
- Consumers: Telegram Bot API
- Method/path: `POST /telegram/webhook`
- Request: Telegram Update JSON (message.text)
- Response: 200 OK after process attempt
- Auth: optional `X-Telegram-Bot-Api-Secret-Token` = `Telegram__WebhookSecretToken`
- Notes: maps to assistant-api chat; never forwards bot token

## File Contract Template

```markdown
## File Flow: <name>

- Owner:
- Bucket:
- Object key strategy:
- Metadata:
- Upload:
- Download:
- Access control:
- Retention:
- Scanning:
```

