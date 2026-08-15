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
  - `agentId` optional: resume Cursor agent
  - reserved optional later: `attachments[]`, `tools[]`, `retrieval`, `provider`
- Response:
  - `schemaVersion: 1`
  - `conversationId`
  - `messageId`
  - `text`
  - `provider`: `stub` | `cursor-sdk`
  - `agentId` optional (when Cursor path used or echoed on stub resume)
  - `intent` optional (resolved/classified)
- Errors: ProblemDetails
- Auth: `X-Service-Key` header = `Assistant__ServiceKey`
- Rate/size limits: text max 4000; later file size in files phase
- Backward compatibility: additive optional fields only
- Implemented: `src/AssistantApi` + gateway proxy `POST /api/miniapp/chat`
- Harness Phase 2: classify → specialist prompt (`@cursor/sdk`) → soft verify; stub fallback without key
- Harness Phase 3 (planned): router pack → inject profile+domain episodes → domain pack runtime → hard verify → persist episode; `agentId` affinity per domain; optional response `domainPack`

## API: cursor-sdk-bridge.run

- Owner: assistant-api / cursor-sdk-bridge
- Consumers: assistant-api only (compose-internal)
- Method/path: `POST /v1/run`
- Request: `{ apiKey, prompt, agentId?, model? }`
- Response: `{ agentId, text }`
- Auth: internal network trust; apiKey per-call, not stored
- Notes Phase 2: wraps `@cursor/sdk` Agent.create / Agent.resume + send/wait; cloud no-repo; body = prompt only
- Phase 3 (planned): pack runtime — `packId`, cwd/skills, MCP allowlist, model; still internal-only; apiKey per-call

## API: telegram-gateway.webhook

- Owner: telegram-gateway
- Consumers: Telegram Bot API
- Method/path: `POST /telegram/webhook`
- Request: Telegram Update JSON (message.text)
- Response: 200 OK after process attempt
- Auth: optional `X-Telegram-Bot-Api-Secret-Token` = `Telegram__WebhookSecretToken`
- Notes: maps to assistant-api chat; never forwards bot token

## Data: assistant-api.harness-memory (Phase 3 planned)

- Owner: assistant-api
- Consumers: assistant-api only (inject into pack prompt); not gateway, not bridge store
- Profile: `{ userId, displayName?, locale?, timezone?, notes? }` — shared, short
- Episode: `{ userId, domain, task, result, at, conversationId?, traceId? }` — per-domain, brief
- Consistency: strong внутри assistant-api
- Isolation: read episodes WHERE domain = current pack; never cross-inject
- Retention: cap last K per (userId, domain); TTL — отдельным решением
- Not: embeddings, full messages[], Cursor agent transcript

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

