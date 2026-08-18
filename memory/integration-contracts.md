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
- Request: `{ apiKey, prompt, agentId?, model?, packId?, localCwd?, collectImages?, imageCap? }`
- Response: `{ agentId, text, packId?, images?, error? }`
- Auth: internal network trust; apiKey per-call, not stored
- Notes Phase 2: cloud no-repo when packId absent
- Phase 3: `packId` → local cwd=`AgentPacks/<id>`, `.cursor/skills`, empty MCP (`mcpServers: {}`)
- Phase 4 research images (ADR-011): `collectImages=true` + `localCwd` under `RESEARCH_IMAGE_VOLUME` → local Agent.create; after wait() scan png|jpg|webp (prefer `out/`), cap ≤14 → `images[]`; soft `error=image-tool-missing` on tool/429. Ordinary `/v1/chat` may stay cloud/no-repo when packId absent.

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
- Consumers: marketing pack (inject ✅); telegram-gateway (`/research`, Mini App settings) via research APIs ✅
- Source: Instagram Graph API own account only (ADR-009). No Apify. Client: `IInstagramGraphClient` / `HttpInstagramGraphClient` ✅
- Settings table `research_settings`: `userId`, `instagramHandle`, `enabled`, `cadenceDays` (default 14), `timezone`, `notifyChatId`, `nextRunAt`, `lastRunAt`, `lastError?` — **no raw IG token in row**
- Artifacts (Postgres, ADR-010 — **not RAG**):
  - `research_snapshots`: normalized posts + visual notes + summary JSON; retention **last K=5** per user; payload ≤256KB; posts ≤50; no raw token ✅
  - `research_plans`: 14 items `{date, caption, hashtags, imagePrompt, mediaPath?, telegramFileId?, status}`; retention **last K=3** ✅
  - harness `harness_episodes` domain=`marketing` after capture («Research … → план») ✅
  - `research_schedule_runs`: unique `(userId, periodKey)` successful windows ✅
- Capture: `IInstagramResearchCapture` — Graph fetch (≤50 media) → snapshot → plan → episode; soft-fail persist
- Scheduler: `IResearchSchedulerJob` / `ResearchSchedulerHostedService` — due `enabled && nextRunAt≤now`; cadenceDays default 14; success → nextRunAt+=cadence, lastRunAt, clear lastError, mark period; Graph fail → lastError, keep snapshot, no period mark; no token/disabled → no-op; notify = `IResearchNotifyHook` (`GatewayResearchNotifyHook` when `Gateway:BaseUrl` set) ✅
- Inject: `IResearchPackInjector` — latest snapshot summary + last plan **only** into marketing pack (salon isolation)
- Bot: `/research` on|off|account|now|plan|status ✅
- Mini App: research settings (no IG token) ✅; mutations require initData HMAC ✅
- Images: Cursor GenerateImage via local marketing-pack + volume (ADR-011) ✅; cap 14; soft-fail `image-tool-missing`; mediaPath on plan items; notify may include `photoPaths`
- Backward compatibility: additive `/v1/chat` fields only if needed; `schemaVersion` unchanged

## API: assistant-api.research

- Owner: assistant-api
- Consumers: telegram-gateway (Mini App + bot)
- Methods:
  - `GET /v1/research/settings?userId=tg-*`
  - `PUT /v1/research/settings` body `{ userId, enabled?, instagramHandle?, cadenceDays?, timezone?, notifyChatId? }`
  - `POST /v1/research/run` body `{ userId, notifyChatId? }` — force capture (period `manual-…`)
  - `GET /v1/research/latest?userId=tg-*` — settings + snapshot summary + plan preview + **additive** `analytics` + `posts[]` + `items[]` + `source` (`instagram`|`vk`) (Phase 5; schemaVersion unchanged)
- Auth: `X-Service-Key`
- Validation: userId must be `tg-<digits>`; reject secret-like handle/token fields; no IG token in request/response
- Errors: ProblemDetails 400/401
- latest.items[].imageUrl: null from assistant-api; gateway fills media proxy URL

## API: telegram-gateway.research-proxy

- Owner: telegram-gateway
- Methods: `GET/PUT /api/miniapp/research/settings`, `POST /api/miniapp/research/run`, `GET /api/miniapp/research/latest`, `GET /api/miniapp/research/media?path=`
- Auth: browser → gateway (service key server-side); **mutations** require Telegram Mini App `initData` HMAC (`X-Telegram-Init-Data` or body `initData`) + `tg-*` userId matching initData user (not prefix alone)
- Reads (`GET` settings/latest): `tg-*` userId validation (no anonymous)
- Media (`GET /media`): initData HMAC (header preferred; query `initData` fallback for img) + `ResearchPhotoPathGuard`; no IG token in URL; deny traversal
- `POST /internal/notify` `{ chatId, text, photoPaths?, imageVolumePath? }` — `X-Service-Key`; sendMessage always; sendPhoto from shared volume (path guard); fail photos ≠ fail text
- Bot UX (ADR-012): `TELEGRAM__WEBAPPURL` → InlineKeyboard web_app + MenuButtonWebApp; `/research plan` ≤3 photos + studio button
- Studio hardening (`phase5-ui-hardening`): posts feed, `source` badge, VK allowlist ≤10, gallery ≤14, a11y/limits

## External: instagram-graph (Phase 4)

- Owner: assistant-api adapter (`AssistantApi.Instagram`)
- Auth: `INSTAGRAM__ACCESSTOKEN` (+ `INSTAGRAM__IGUSERID` / `BUSINESSACCOUNTID`) from env/secret store; AES-GCM encrypt-at-rest (`EncryptedInstagramTokenStore`); master = `Instagram:MasterKey` or shared `Cursor:MasterKey`
- Scope: own account media (`caption,media_url,timestamp,permalink,media_type`) + optional insights when scope allows
- Fetch limit: default 25, hard cap **50** (`InstagramFetchLimits`)
- Media download: SSRF allowlist `*.cdninstagram.com` / `*.fbcdn.net` + size limit
- Failure: no token → stub skip; rate-limit / token expiry → soft error codes (`instagram-rate-limited` / `instagram-token-expired`); no secret leak in logs/messages
- Non-goals: foreign profiles, Apify, unofficial mobile API

## External: vk-wall (Phase 6)

- Owner: assistant-api adapter (`AssistantApi.Vk`: `IVkWallClient` / `HttpVkWallClient` / `FallbackVkWallClient`)
- Auth: `VK__SERVICETOKEN` from env/secret store; AES-GCM encrypt-at-rest (`EncryptedVkTokenStore`); master = `Vk:MasterKey` or shared Instagram/Cursor MasterKey
- Methods (official API only): `utils.resolveScreenName` → `wall.get` for open communities; stub skip without token
- Payload: post `text` + photo attachments; Donut posts soft-skipped in mapper; closed wall → `vk-wall-closed`; no comments / author profiles
- Cap: ≤50 posts (`VkFetchLimits`, align with IG); additive snapshot field `source=vk` ✅ (`IVkResearchCapture` / `ResearchSnapshotBuilder.FromVkFetch`)
- CDN SSRF allowlist `*.userapi.com` (+ no IP literals) ✅; media download → volume ✅ (`IVkMediaDownloader` / `VkPhotoStore`; plan MediaPath; gateway proxy)
- ApiBaseUrl: only `https://api.vk.com/...` (`VkApiHostGuard` + options ValidateOnStart) ✅
- MediaPath: relative volume paths only (`ResearchMediaPathGuard`; serialize/ApplyMediaPaths strip CDN/absolute) ✅
- Failure: no token → stub skip; rate-limit / token invalid / closed → soft error codes; no secret leak in logs/messages
- Capture: `IVkResearchCapture` — wall fetch → snapshot+plan+marketing episode; soft-fail persist; inject via existing `IResearchPackInjector` (source= shown); photos → volume soft-skip without `Research:ImageVolumePath`
- Settings: additive `vkCommunities[]` (screen_name / owner_id) on research settings DTO; `POST /v1/research/run` optional `source=vk` → CaptureAllowlist; Mini App + `/research vk add|remove|list|now`; never accept service token from UI/chat ✅
- Caps: allowlist ≤10 communities; photos/capture ≤14; retention SnapshotCap=5 PlanCap=3
- Non-goals: Apify, HTML/`m.vk.com`, user VK ID OAuth, separate `vk-research-api`
- Status: **closed** (`phase6-vk-hardening` ✅)

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

