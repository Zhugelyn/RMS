# Service Catalog

Проект `telegram-ai`. Phase 3 packs functionally closed. Phase 4 = Marketing Instagram Research (`phase4-miniapp-research` ✅ → next `phase4-generate-image`).

## Service: telegram-gateway

- Owner: telegram-bot-agent
- Business capability: Telegram chat + Mini App UI; доставка запросов в assistant-api
- Code: `src/TelegramGateway`
- Public API: `POST /telegram/webhook`; `POST /api/miniapp/chat`; Mini App research proxy `GET/PUT /api/miniapp/research/settings`, `POST /api/miniapp/research/run`, `GET /api/miniapp/research/latest`; internal `POST /internal/notify` (X-Service-Key); static Mini App (`/`); health
- Bot: `/research` | `on` | `off` | `account @handle` | `now` | `plan` | status
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
- Object storage: Phase 4 — local volume for GenerateImage artifacts (marketing pack); MinIO = Phase 6
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

## Phase 4 — Instagram Research (in progress)

- Capability: 14-day marketing research from own Instagram Graph feed; Mini App settings + bot `/research`
- Feed source: Instagram Graph API only (ADR-009). **Apify — no.** Client: `HttpInstagramGraphClient` + stub/fallback without token ✅
- Token: `INSTAGRAM__ACCESSTOKEN` (+ `IGUSERID`/`BUSINESSACCOUNTID`) env → AES-GCM seal; reject from chat
- Settings schema: `research_settings` (userId, instagramHandle, enabled, cadenceDays=14, timezone, notifyChatId, nextRunAt, lastRunAt, lastError) ✅
- Artifacts: `IResearchArtifactStore` (Postgres/in-mem) — normalized snapshot posts/visual notes (cap last K) + 14-day plan items (`date,caption,hashtags,imagePrompt,mediaPath?,telegramFileId?,status`). Capture: `IInstagramResearchCapture` after Graph fetch. Inject: marketing pack only (`ResearchPackInjector`). Episode domain=`marketing`. Soft-fail persist. **Not RAG.** ✅ (`phase4-research-artifacts`)
- Images: Cursor GenerateImage via local marketing-pack + volume (ADR-011). **Not OpenAI Images.**
- UI: gateway Mini App research settings + `/research` command ✅ (`phase4-miniapp-research`)
- Scheduler: `ResearchSchedulerHostedService` + `ResearchSchedulerJob` — 14d cadence, ListDue, idempotent `research_schedule_runs` (userId+period), lastError on Graph fail, no-op without token/enabled; notify = `GatewayResearchNotifyHook` → gateway `/internal/notify` (NoOp when `Gateway:BaseUrl` empty). No Hangfire. ✅
- Do not add separate `instagram-research-api` until independent ownership

## Reserved (do not implement in Phase 4)

- rag-service / embedding-service / search-elasticsearch (Phase 5)
- files-minio (Phase 6)
- yandex-direct-adapter (Phase 7)
- apify-adapter / foreign-account scrapers
- openai-images-adapter
- per-domain public microservices (`salon-api`, …) — только после независимого ownership
