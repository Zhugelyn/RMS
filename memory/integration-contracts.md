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
  - `domainPack` optional (Phase 3 resolved pack id)
- Errors: ProblemDetails
- Auth: `X-Service-Key` header = `Assistant__ServiceKey`
- Rate/size limits: text max 4000; later file size in files phase
- Backward compatibility: additive optional fields only
- Implemented: `src/AssistantApi` + gateway proxy `POST /api/miniapp/chat`
- Harness Phase 2: classify → specialist prompt (`@cursor/sdk`) → soft verify; stub fallback without key
- Harness Phase 3: router pack (SDK) / pack-hint fallback → inject profile+domain episodes → domain pack local runtime → hard verify → persist episode; affinity per domain; response `domainPack`

## API: cursor-sdk-bridge.run

- Owner: assistant-api / cursor-sdk-bridge
- Consumers: assistant-api only (compose-internal)
- Method/path: `POST /v1/run`
- Request: `{ apiKey, prompt, agentId?, model?, packId? }`
- Response: `{ agentId, text, packId? }`
- Auth: internal network trust; apiKey per-call, not stored
- Notes Phase 2: cloud no-repo when packId absent
- Phase 3: `packId` → local cwd=`AgentPacks/<id>`, `.cursor/skills`, empty MCP (`mcpServers: {}`)

## Data: agent-packs (Phase 3)

- Owner: assistant-api
- Path: `src/AgentPacks/<id>/` (+ `.cursor/skills` for local SDK)
- Product: salon=Babor Брянск growth; marketing=beauty market; tasks=schedule/reminders; `_router` classify-only
- Manifest: `pack.json`; MCP allowlist empty until wiring; `_router` answersUser=false resumePolicy=none
- Loader: `PackCatalog` / `IPackCatalog`

## API: telegram-gateway.webhook

- Owner: telegram-gateway
- Consumers: Telegram Bot API
- Method/path: `POST /telegram/webhook`
- Request: Telegram Update JSON (message.text)
- Response: 200 OK after process attempt
- Auth: optional `X-Telegram-Bot-Api-Secret-Token` = `Telegram__WebhookSecretToken`
- Notes: maps to assistant-api chat; never forwards bot token

## Data: assistant-api.harness-memory (Phase 3 → Phase 4 Postgres)

- Owner: assistant-api
- Impl: `IHarnessMemoryStore` / in-process now; Postgres durable = `phase4-postgres-settings`
- Profile: `{ userId, displayName?, locale?, timezone?, notes? }` — shared, short
- Episode: `{ userId, domain, task, result, at, conversationId?, traceId? }` — per-domain, brief
- Consistency: strong внутри assistant-api process (→ DB transactions after Postgres)
- Isolation: read episodes WHERE domain = current pack; never cross-inject
- Retention: cap last K per (userId, domain)
- Not: embeddings, full messages[], Cursor agent transcript

## Data: assistant-api.instagram-research (Phase 4)

- Owner: assistant-api
- Consumers: marketing pack (inject); telegram-gateway (`/research`, Mini App settings) via assistant APIs
- Source: Instagram Graph API own account only (ADR-009). No Apify.
- Artifacts (Postgres, ADR-010 — **not RAG**):
  - `snapshot`: media/insights slice at run time (structured, size-capped)
  - `plan`: 14-day research/content plan
  - `episodes`: brief task→result for research runs
- Settings: schedule window days (default 14), enabled flags, account binding metadata (no raw token in settings row if sealed separately)
- Bot: `/research` start|status (gateway → assistant)
- Mini App: research settings screen (no IG token in browser)
- Images: GenerateImage via local marketing-pack + volume (ADR-011); paths in artifacts, not base64 dumps in chat
- Backward compatibility: additive `/v1/chat` fields only if needed; `schemaVersion` unchanged

## External: instagram-graph (Phase 4)

- Owner: assistant-api adapter
- Auth: `INSTAGRAM__ACCESSTOKEN` (+ business account id) from env/secret store
- Scope: own account media/insights only
- Failure: rate-limit / token expiry → user-visible soft error; no secret leak
- Non-goals: foreign profiles, Apify, unofficial mobile API

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

