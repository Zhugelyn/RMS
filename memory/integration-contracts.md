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
- Impl: `IHarnessMemoryStore` → `PostgresHarnessMemoryStore` when `ConnectionStrings:AssistantDb` set; else `InMemoryHarnessMemoryStore`
- Tables: `user_profiles`, `harness_episodes` (EF migrations)
- Profile: `{ userId, displayName?, locale?, timezone?, notes? }` — shared, short
- Episode: `{ userId, domain, task, result, at, conversationId?, traceId? }` — per-domain, brief
- Consistency: strong внутри assistant-api (DB transactions when Postgres)
- Isolation: read episodes WHERE domain = current pack; never cross-inject (tests)
- Retention: cap last K per (userId, domain)
- Not: embeddings, full messages[], Cursor agent transcript

## Data: assistant-api.instagram-research (Phase 4)

- Owner: assistant-api
- Consumers: marketing pack (inject — later); telegram-gateway (`/research`, Mini App settings) via assistant APIs (later)
- Source: Instagram Graph API own account only (ADR-009). No Apify. Client: `IInstagramGraphClient` / `HttpInstagramGraphClient` ✅ (`phase4-ig-graph`)
- Settings table `research_settings`: `userId`, `instagramHandle`, `enabled`, `cadenceDays` (default 14), `timezone`, `notifyChatId`, `nextRunAt`, `lastRunAt` — **no raw IG token in row**
- Artifacts (Postgres stubs, ADR-010 — **not RAG**):
  - `research_snapshots`: media/insights slice placeholder — persist = `phase4-research-artifacts`
  - `research_plans`: 14-day plan placeholder
  - harness `harness_episodes` for brief task→result (research domain later)
- Bot: `/research` start|status (gateway → assistant) — not this slice
- Mini App: research settings screen (no IG token in browser) — not this slice
- Images: GenerateImage via local marketing-pack + volume (ADR-011) — not this slice
- Backward compatibility: additive `/v1/chat` fields only if needed; `schemaVersion` unchanged

## External: instagram-graph (Phase 4)

- Owner: assistant-api adapter (`AssistantApi.Instagram`)
- Auth: `INSTAGRAM__ACCESSTOKEN` (+ `INSTAGRAM__IGUSERID` / `BUSINESSACCOUNTID`) from env/secret store; AES-GCM encrypt-at-rest (`EncryptedInstagramTokenStore`); master = `Instagram:MasterKey` or shared `Cursor:MasterKey`
- Scope: own account media (`caption,media_url,timestamp,permalink,media_type`) + optional insights when scope allows
- Media download: SSRF allowlist `*.cdninstagram.com` / `*.fbcdn.net` + size limit
- Failure: no token → stub skip; rate-limit / token expiry → soft error codes (`instagram-rate-limited` / `instagram-token-expired`); no secret leak in logs/messages
- Non-goals: foreign profiles, Apify, unofficial mobile API, snapshot persist (next slice)

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

