# Service Catalog

Проект `telegram-ai`. Phase 3 packs functionally closed. Phase 4 = Marketing Instagram Research (docs → postgres-settings next).

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
- Business capability: chat completion + Phase 2 Cursor SDK harness
- Code: `src/AssistantApi`
- Public API: `POST /v1/chat`, `GET /health/live`, `GET /health/ready`
- Events produced: нет
- Events consumed: нет
- Database: affinity in-memory; harness memory in-process today. Phase 4: PostgreSQL — `user_profiles`, `harness_episodes`, research `snapshot`/`plan`/`episodes` + settings (owner assistant-api)
- Object storage: Phase 4 — local volume for GenerateImage artifacts (marketing pack); MinIO = Phase 6
- External dependencies: `ILlmProvider` = `FallbackLlmProvider` (`CursorSdkLlmProvider` → stub); internal `cursor-sdk-bridge`; Phase 4 — Instagram Graph API (own account)
- Security notes: inter-service auth `X-Service-Key`; rejects secret-like chat text; Cursor API key encrypt-at-rest (AES-GCM); IG token env/secret store only, never in logs/response/Telegram

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
- Memory: `IHarnessMemoryStore` in-process; Postgres durable = `phase4-postgres-settings`.

## Phase 4 — Instagram Research (planned ownership)

- Capability: 14-day marketing research from own Instagram Graph feed; Mini App settings + bot `/research`
- Feed source: Instagram Graph API only (ADR-009). **Apify — no.**
- Artifacts: snapshot + plan + episodes in Postgres (ADR-010). **Not RAG.**
- Images: Cursor GenerateImage via local marketing-pack + volume (ADR-011). **Not OpenAI Images.**
- UI: gateway Mini App research settings + `/research` command
- Do not add separate `instagram-research-api` until independent ownership

## Reserved (do not implement in Phase 4)

- rag-service / embedding-service / search-elasticsearch (Phase 5)
- files-minio (Phase 6)
- yandex-direct-adapter (Phase 7)
- apify-adapter / foreign-account scrapers
- openai-images-adapter
- per-domain public microservices (`salon-api`, …) — только после независимого ownership
