# Service Catalog

Проект `telegram-ai`. Phase 4/5/6/7/8 closed. Next = Phase 9 External tools (Яндекс Директ) — только по явному запросу.

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
- External dependencies: `ILlmProvider` = `FallbackLlmProvider` (`CursorSdkLlmProvider` → stub); internal `cursor-sdk-bridge`; Instagram Graph API own account (`IInstagramGraphClient`, ADR-009); optional notify → gateway (`Gateway__BaseUrl`); Phase 7 → `rag-service` HTTP (`Rag:BaseUrl` + `X-Service-Key`, never ES)
- Security notes: inter-service auth `X-Service-Key`; rejects secret-like chat text (incl. IG token patterns); Cursor API key + IG token encrypt-at-rest (AES-GCM); IG token env/secret store only, never in logs/response/Telegram/Mini App; Postgres password env-only; research userId must be `tg-*`; RAG service key env-only

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

- Layout: `src/AgentPacks/{salon,marketing,tasks,_router}` — `AGENTS.md`, `skills/` + `.cursor/skills`, `prompts/`, `mcp.json`, `pack.json`.
- Product: salon=Babor (Брянск) growth; marketing=beauty market; tasks=schedule/reminders; `_router` classify-only.
- Schema: `src/AgentPacks/pack.schema.json`.
- Loader: `PackCatalog`; runtime: bridge local cwd per packId; affinity + harness memory in assistant-api.
- Memory: `IHarnessMemoryStore` → Postgres when CS set, else in-process; domain episode isolation.
- Phase 7 RAG MCP: salon/marketing allowlist `kb-retriever` + skill `kb-retrieve`; `_router`/`tasks` empty; inject via `RagPackInjector` ✅

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

## Phase 5 — Research Client UI (closed)

- Capability: Mini App studio + bot web_app over existing research API (ADR-012). Not RAG.
- latest DTO: analytics + posts[] + items[] + additive `source` (+ planPreview); imageUrl = media proxy
- Hardening ✅ (`phase5-ui-hardening`): a11y/limits/VK allowlist+gallery; Phase 5 closed 2026-08-18

## Phase 6 — VK Public Research (closed)

- Capability: open VK community walls via official API (`wall.get`); text + photo attachments; service token (ADR-013)
- Docs ✅; Client ✅; Artifacts ✅; Settings ✅; Media ✅; Hardening ✅ (`VkApiHostGuard`, `ResearchMediaPathGuard`, caps, non-goals tests, token rotation)
- Owner: assistant-api adapter (alongside Instagram Graph); do not add separate `vk-research-api`
- Secrets: `VK__SERVICETOKEN` env/secret store + AES-GCM; not from chat/Mini App
- Photos: CDN `*.userapi.com` download to `Research:ImageVolumePath`; relative MediaPath only (no durable CDN URLs)
- ApiBaseUrl: `api.vk.com` only; allowlist ≤10; wall ≤50; photos ≤14

## Service: elasticsearch (Phase 7 data plane)

- Owner: `rag-service`
- Image: `docker.elastic.co/elasticsearch/elasticsearch:8.15.3` via compose
- Internal only `:9200`; health `_cluster/health` (yellow|green)
- Volume: `elasticsearch-data`
- Connected only via `rag-service` (assistant-api / gateway / bridge do **not** query ES)
- Security: xpack.security **enabled** + basic auth (`ELASTIC_PASSWORD`); HTTP SSL off (compose-internal); not published

## Service: rag-service (Phase 7)

- Owner: index + ingest/search HTTP API (ADR-014)
- Endpoints: `POST /v1/ingest`, `POST /v1/search`, `GET /health/live|ready`
- Auth: `X-Service-Key` (`Rag:ServiceKey` / `RAG__SERVICEKEY`)
- Indexes: `kb-salon` / `kb-marketing`; stub embedder (no SaaS key)
- Compose: depends_on healthy `elasticsearch`; internal `:8080`
- Consumers: assistant-api HTTP only (`HttpRagRetriever` / pack inject) ✅
- Soft-fail search → empty hits
- Pack retriever ✅ (`phase7-pack-retriever`): `HttpRagRetriever` + `RagPackInjector`; compose `Rag__BaseUrl`/`Rag__ServiceKey`
- Hardening ✅ (`phase7-hardening`): `RagLimits`, PII-safe logs, ES basic auth, isolation + non-goals tests

## Phase 7 — Knowledge / RAG (closed ✅)

- Docs ✅ (`phase7-docs`, ADR-014): RAG ≠ harness ≠ research; domain-split indexes; retriever via pack MCP
- ES compose ✅ (`phase7-es-compose`): container + health
- rag-api ✅ (`phase7-rag-api`): ingest/search + domain isolation + stub embedder + service key
- Pack retriever ✅ (`phase7-pack-retriever`)
- Hardening ✅ (`phase7-hardening`): caps, PII logs, ES basic auth, non-goals
- Soft-fail: empty retrieval must not fail `/v1/chat`
- Embeddings: stub OK until dedicated slice
- Non-goals: MinIO (8), Direct (9), Apify, mixing indexes, replacing harness/research with RAG

## Phase 8 — Files / MinIO (open)

- Docs ✅ (`phase8-docs`, ADR-015): MinIO private + presign; metadata+authz в assistant-api Postgres; pack MCP salon/marketing; domain prefix isolation; no byte proxy
- Compose ✅ (`phase8-minio-compose`): `minio` + `minio-init` (private `tg-ai-salon` / `tg-ai-marketing`); internal ports; health; volume `minio-data`
- Presign ✅ (`phase8-presign`): `file_objects` metadata; `POST /v1/files/upload-intent`, confirm, download-url; MinIO SDK short-TTL PUT/GET; size/MIME; opaque keys; `X-Service-Key`; soft-fail without MinIO
- Pack files ✅ (`phase8-pack-files`): MCP `files` + skill `files-store` in salon/marketing; `FilesPackInjector` metadata inject; router/tasks empty; domain isolation
- Hardening ✅ (`phase8-hardening`): TTL clamps; scan stub; token rotation; non-goals guard
- Status: **Phase 8 closed**
- Owner: MinIO objects; metadata+presign in assistant-api; pack MCP salon/marketing
- Non-goals: Direct (9), Apify, public bucket, proxy large bytes through API, research-volume migration

## Reserved (do not implement until later slices)

- yandex-direct-adapter (Phase 9)
- apify-adapter / HTML / `m.vk.com` scrapers
- vk-id-user-oauth (unless service token insufficient — follow-up, not this phase)
- openai-images-adapter
- per-domain public microservices (`salon-api`, …) — только после независимого ownership
- отдельный marketing website вне Telegram
