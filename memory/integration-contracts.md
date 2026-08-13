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
- Auth: service key / HMAC, TLS
- Rate/size limits: text size cap; later file size in files phase
- Backward compatibility: additive optional fields only

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

