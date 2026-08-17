# Service Catalog

Проект `telegram-ai`. Phase 3 packs functionally closed. Phase 4 = Marketing Instagram Research (`phase4-ig-graph` ✅ → next `phase4-research-artifacts`).

## Service: telegram-gateway

- Owner: telegram-bot-agent
- Business capability: Telegram chat + Mini App UI; доставка запросов в assistant-api
- Code: `src/TelegramGateway`
- Public API: `POST /telegram/webhook`; `POST /api/miniapp/chat`; static Mini App (`/`); health; Phase 4 — `/research` + research settings UI (planned)
- Events produced: нет в Phase 1
- Events consumed: нет
- Database: optional mapping telegramUserId -> userId (пока inline `tg-{id}`)
- Object storage: нет
- External dependencies: Telegram Bot API, assistant-api
- Security notes: владеет `Telegram:BotToken`; не хранит Cursor API key / Instagram token; SecretScanner отклоняет секреты из сообщений; Mini App без секретов; HttpClient logging для Telegram отключён

## Service: assistant-api

- Owner: ai-assistant-agent
- Business capability: chat completion + Phase 2 Cursor SDK harness + durable harness/research settings (Postgres)
- Code: `src/AssistantApi`
- Public API: `POST /v1/chat`, `GET /health/live`, `GET /health/ready`
- Events produced: нет
- Events consumed: нет
- Database: PostgreSQL (compose `postgres`, owner=assistant-api): `user_profiles`, `harness_episodes`, `research_settings`, stub `research_snapshots`/`research_plans`. Affinity still in-memory. Connection string only env (`ConnectionStrings__AssistantDb`). Without CS → in-process stores.
- Object storage: Phase 4 — local volume for GenerateImage artifacts (marketing pack); MinIO = Phase 6
- External dependencies: `ILlmProvider` = `FallbackLlmProvider` (`CursorSdkLlmProvider` → stub); internal `cursor-sdk-bridge`; Instagram Graph API own account (`IInstagramGraphClient`, ADR-009)
- Security notes: inter-service auth `X-Service-Key`; rejects secret-like chat text (incl. IG token patterns); Cursor API key + IG token encrypt-at-rest (AES-GCM); IG token env/secret store only, never in logs/response/Telegram; Postgres password env-only

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
- Settings schema: `research_settings` (userId, instagramHandle, enabled, cadenceDays=14, timezone, notifyChatId, nextRunAt, lastRunAt) ✅
- Artifacts tables stubbed: snapshot + plan (ADR-010). **Not RAG.** Persist/inject = `phase4-research-artifacts`
- Images: Cursor GenerateImage via local marketing-pack + volume (ADR-011). **Not OpenAI Images.**
- UI: gateway Mini App research settings + `/research` command (later slices)
- Do not add separate `instagram-research-api` until independent ownership

## Reserved (do not implement in Phase 4)

- rag-service / embedding-service / search-elasticsearch (Phase 5)
- files-minio (Phase 6)
- yandex-direct-adapter (Phase 7)
- apify-adapter / foreign-account scrapers
- openai-images-adapter
- per-domain public microservices (`salon-api`, …) — только после независимого ownership
