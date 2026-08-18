# Service Catalog

Проект `telegram-ai`. Phase 4 Instagram Research closed. Phase 5 studio ✅ (`phase5-ui-hardening` deferred). Phase 6 = VK Public Research (next=`phase6-vk-docs`). RAG = Phase 7.

## Service: telegram-gateway

- Owner: telegram-bot-agent
- Business capability: Telegram chat + Mini App UI; доставка запросов в assistant-api
- Code: `src/TelegramGateway`
- Public API: `POST /telegram/webhook`; `POST /api/miniapp/chat`; Mini App research proxy `GET/PUT /api/miniapp/research/settings`, `POST /api/miniapp/research/run`, `GET /api/miniapp/research/latest`, `GET /api/miniapp/research/media` (initData + path guard); internal `POST /internal/notify` (X-Service-Key); static Mini App Research Studio (`/`); health
- Bot: `/research` | `on` | `off` | `account @handle` | `now` | `plan` | status; InlineKeyboard web_app «Открыть студию» + MenuButtonWebApp when `TELEGRAM__WEBAPPURL`
- Events produced: нет в Phase 1
- Events consumed: нет
- Database: optional mapping telegramUserId -> userId (пока inline `tg-{id}`)
- Object storage: нет
- External dependencies: Telegram Bot API, assistant-api
- Security notes: владеет `Telegram:BotToken`; не хранит Cursor API key / Instagram token; SecretScanner отклоняет секреты из сообщений; Mini App без IG token; research mutations требуют `tg-*`; HttpClient logging для Telegram отключён

## Service: assistant-api

- Owner: ai-assistant-agent
- Business capability: chat completion + Phase 2 Cursor SDK harness + durable harness/research settings (Postgres) + research APIs
- Code: `src/AssistantApi`
- Public API: `POST /v1/chat`; `GET/PUT /v1/research/settings`; `POST /v1/research/run`; `GET /v1/research/latest`; `GET /health/live`, `GET /health/ready`
- Events produced: нет
- Events consumed: нет
- Database: PostgreSQL (compose `postgres`, owner=assistant-api): `user_profiles`, `harness_episodes`, `research_settings`, `research_snapshots`, `research_plans`, `research_schedule_runs`. Affinity still in-memory. Connection string only env (`ConnectionStrings__AssistantDb`). Without CS → in-process stores.
- Object storage: Phase 4 — local volume for GenerateImage artifacts (marketing pack); VK downloads reuse volume in Phase 6; MinIO = Phase 8
- External dependencies: `ILlmProvider` = `FallbackLlmProvider` (`CursorSdkLlmProvider` → stub); internal `cursor-sdk-bridge`; Instagram Graph API own account (`IInstagramGraphClient`, ADR-009); optional notify → gateway (`Gateway__BaseUrl`)
- Security notes: inter-service auth `X-Service-Key`; rejects secret-like chat text (incl. IG token patterns); Cursor API key + IG token encrypt-at-rest (AES-GCM); IG token env/secret store only, never in logs/response/Telegram/Mini App; Postgres password env-only; research userId must be `tg-*`

## Service: postgres (assistant-api data plane)

- Owner: assistant-api (database-per-service)
- Image: `postgres:16-alpine` via compose
- Not shared with gateway; no cross-service table reads

## Service: cursor-sdk-bridge (internal)

- Owner: ai-assistant-agent
- Business capability: thin Node HTTP wrapper around `@cursor/sdk` (create/resume/send/wait)
- Code: `src/CursorSdkBridge`
- Public API: нет (compose-internal `:8090`)
- Auth: receives decrypted API key per-request from assistant-api over internal network; does not persist key
- Security notes: non-root; health only; no public ports

## Domain packs (Phase 3)

- Layout: `src/AgentPacks/{salon,marketing,tasks,_router}` — `AGENTS.md`, `skills/` + `.cursor/skills`, `prompts/`, `mcp.json` (empty allowlist), `pack.json`.
- Product: salon=Babor (Брянск) growth; marketing=beauty market; tasks=schedule/reminders; `_router` classify-only.
- Schema: `src/AgentPacks/pack.schema.json`.
- Loader: `PackCatalog`; runtime: bridge local cwd per packId; affinity + harness memory in assistant-api.
- Memory: `IHarnessMemoryStore` → Postgres when CS set, else in-process; domain episode isolation.

## Phase 4 — Instagram Research (closed)

- Capability: 14-day marketing research from own Instagram Graph feed; Mini App settings + bot `/research`
- Feed source: Instagram Graph API only (ADR-009). **Apify — no.** Client: `HttpInstagramGraphClient` + stub/fallback without token ✅
- Token: `INSTAGRAM__ACCESSTOKEN` (+ `IGUSERID`/`BUSINESSACCOUNTID`) env → AES-GCM seal; reject from chat
- Settings schema: `research_settings` (userId, instagramHandle, enabled, cadenceDays=14, timezone, notifyChatId, nextRunAt, lastRunAt, lastError) ✅
- Artifacts: `IResearchArtifactStore` (Postgres/in-mem) — normalized snapshot posts/visual notes (cap last K=5) + 14-day plan (cap K=3). Capture: `IInstagramResearchCapture` after Graph fetch. Inject: marketing pack only (`ResearchPackInjector`). Episode domain=`marketing`. Soft-fail persist. **Not RAG.** ✅
- Images: Cursor GenerateImage via local marketing-pack + Docker volume `research-images` (`Research:ImageVolumePath` / `RESEARCH__IMAGEVOLUMEPATH`) (ADR-011). Bridge `collectImages` → `images[]` cap 14; soft-fail `image-tool-missing`; mediaPath on plan; gateway `sendPhoto`. **Not OpenAI Images.** ✅
- UI: gateway Mini App Research Studio + `/research` + web_app (ADR-012) ✅ (`phase5-research-ui`); Mini App mutations require initData HMAC ✅ (`phase4-hardening`)
- Scheduler: `ResearchSchedulerHostedService` + `ResearchSchedulerJob` — 14d cadence, ListDue, idempotent `research_schedule_runs` (userId+period), lastError on Graph fail / image soft-fail, no-op without token/enabled; notify = `GatewayResearchNotifyHook` → gateway `/internal/notify` (+ optional photos). No Hangfire. ✅
- Hardening: Graph fetch ≤50; payload size; path traversal; retention caps; token rotation README; non-goals guard tests ✅
- Do not add separate `instagram-research-api` until independent ownership

## Phase 5 — Research Client UI

- Capability: Mini App studio + bot web_app over existing research API (ADR-012). Not RAG.
- latest DTO: analytics + posts[] + items[] (+ planPreview); imageUrl = media proxy
- leftover: `phase5-ui-hardening` deferred

## Phase 6 — VK Public Research (open)

- Capability: open VK community walls via official API (`wall.get`); text + photo attachments; service token (ADR-013)
- Docs ✅ (`phase6-vk-docs`): ADR-013 + README/catalog/contracts/security/.env.example (`VK__SERVICETOKEN=`)
- Next slice: `phase6-vk-client` (`IVkWallClient` / resolveScreenName + wall.get; no service code yet)
- Owner: assistant-api adapter (alongside Instagram Graph); do not add separate `vk-research-api`
- Secrets: `VK__SERVICETOKEN` env/secret store only; not from chat/Mini App

## Reserved (do not implement in Phase 6)

- rag-service / embedding-service / search-elasticsearch (Phase 7)
- files-minio (Phase 8)
- yandex-direct-adapter (Phase 9)
- apify-adapter / HTML / `m.vk.com` scrapers
- vk-id-user-oauth (unless service token insufficient — follow-up, not this phase)
- openai-images-adapter
- per-domain public microservices (`salon-api`, …) — только после независимого ownership
- отдельный marketing website вне Telegram
