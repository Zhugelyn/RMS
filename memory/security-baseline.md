# Security Baseline

## Обязательный минимум

- Authn/authz для public API, privileged operations, consumers команд и file operations.
- Secrets только через env, Docker secrets, user-secrets, Kubernetes Secrets или external secret provider. Не в git, не в Mini App, не в OpenAPI examples.
- TLS на ingress. Internal trust model фиксировать явно.
- Input validation для HTTP payloads, query params, headers, message payloads, file metadata и object keys.
- Structured logs без secrets, tokens, passwords, raw PII и содержимого файлов.
- Non-root containers, minimal images, resource limits, probes и securityContext.

## File Security

- Private buckets by default.
- Presigned URLs с коротким TTL.
- Size limits и content validation.
- Object keys без PII и predictable IDs.
- Scanning hook для untrusted uploads.

## Messaging Security

- Producers и consumers должны иметь минимальные permissions.
- Events/commands имеют schema version и owner.
- Replay и poison message impact должны быть описаны.

## Telegram / Cursor tokens

- `Telegram:BotToken` принадлежит только `telegram-gateway`.
- `Cursor:ApiKey` принадлежит только `assistant-api` (+ per-call to internal bridge).
- Не принимать токены из chat message, query, filename, webhook body пользователя.
- Webhook: проверять Telegram secret token header.
- Inter-service: отдельный service credential, не bot token.
- Encrypt-at-rest: AES-GCM (`ISecretProtector` / `EncryptedCursorApiKeyStore`). Master key = `Cursor:MasterKey` env. Plaintext bootstrap scrubbed after seal.
- Логи: никогда bot token, Cursor key, Authorization, raw Telegram update с токенами.
- Rotate: смена secret не требует смены API contract.

## Phase 2 controls (implemented)

- Optional `CURSOR__APIKEY` + required `CURSOR__MASTERKEY` when key present.
- Key sealed at startup; bridge receives decrypted key only over internal compose network for `/v1/run`.
- Fallback to stub when key absent or SDK/bridge fails.
- Harness verify rejects secret-like specialist output.
- Bridge container non-root uid 10001; no public published port.

## Phase 1 controls (implemented)

- `.env` gitignored; `.env.example` без реальных значений.
- `X-Service-Key` на `assistant-api` (health публичный).
- Gateway `SecretScanner` + assistant reject secret-like chat text.
- Telegram HttpClient logging disabled (`ITelegramBotClient` filter) — token в path не логируется.
- Containers: non-root uid 10001, healthchecks, no secrets in images.
- Nested Docker: `scripts/compose-up.sh` включает `ip_forward`/`br_netfilter` для bridge DNS.

## Phase 3 controls

- Pack layout + `PackCatalog` validator: empty MCP allowlist; reject secret-like keys in `mcp.json`; `_router` answersUser=false / resumePolicy=none.
- Bridge local pack cwd; `mcpServers: {}` until allowlist wiring; no ambient MCP.
- Affinity per `conversationId+domain`; cross-domain resume запрещён (client agentId не форсирует чужой домен).
- Hard verify: empty / secret-leak / domain-drift → repair or fail.
- Harness memory: эпизоды изолированы по domain; profile shared; episode write best-effort after verify.

## Phase 4 controls

- `INSTAGRAM__ACCESSTOKEN` только env/secret store / AES-GCM encrypt-at-rest (`EncryptedInstagramTokenStore`); не из chat, Mini App, query, webhook body. ✅ (`phase4-ig-graph`)
- Graph API = own account only; Apify и foreign scrapers запрещены (ADR-009). Client: `HttpInstagramGraphClient` + stub without token.
- Media download SSRF: allowlist `*.cdninstagram.com` / `*.fbcdn.net`; no IP literals; size limit; no auto-redirect off-list.
- Rate-limit / token expiry → soft error codes; never log/return raw token or Graph error bodies with secrets.
- SecretScanner + `/v1/chat` reject IG token patterns (`INSTAGRAM__ACCESSTOKEN`, `IGQVJ`, `access_token=`, EAA…).
- Research artifacts in Postgres (snapshot/plan + settings) — не логировать raw dumps/tokens (ADR-010); persist/inject ✅; soft-fail; marketing-only inject.
- Scheduler: `lastError` sanitized (no token fragments); idempotent period keys; tick exceptions swallowed by BackgroundService; Graph fail не валит host. ✅ (`phase4-scheduler`)
- GenerateImage через Cursor + local marketing-pack volume; без отдельного OpenAI Images secret (ADR-011) ✅ (`phase4-generate-image`)
- Mini App research settings без IG token в браузере; `/research` не принимает токены в тексте ✅ (`phase4-miniapp-research`)
- Research mutations: Mini App = Telegram **initData HMAC** (`TelegramInitDataValidator`, header `X-Telegram-Init-Data`) + userId must match; still require `tg-*`; bot `/research` = Telegram update path (no initData). SecretScanner on handle/settings; gateway `/internal/notify` requires `X-Service-Key` ✅ (`phase4-hardening`)
- Media proxy: `GET /api/miniapp/research/media` requires initData; `ResearchPhotoPathGuard` deny traversal; imageUrl = proxy only (ADR-012) ✅ (`phase5-research-ui`)
- Notify: `GatewayResearchNotifyHook` soft-fail; bot token never leaves gateway; sendPhoto from shared volume with path traversal guard; text always sent even if photos=0
- Volume path: `Research:ImageVolumePath` / `RESEARCH__IMAGEVOLUMEPATH`; no path traversal (workspace + notify + media proxy guards); size limits on generated images before Telegram send; without Cursor key — skip images, stack alive
- Limits: Graph fetch ≤50 (`InstagramFetchLimits`); image cap 14; snapshot/plan PayloadJson ≤256KB; posts/snapshot ≤50; retention last K snapshots=5 / plans=3; bot plan photos ≤3
- Token rotation notes in README (bot / webhook secret / service key / Cursor / IG); never log secret values
- Non-goals guard tests: no Apify / OpenAI Images / ES / embeddings / MinIO / Yandex Direct package wiring
- Postgres: credentials только env (`POSTGRES__PASSWORD` / `ConnectionStrings__AssistantDb`); migrations без secrets in git; database-per-service owner=assistant-api; ready fails if CS set but DB down; without CS → in-process fallback.


## Phase 6 controls (closed ✅)

- `VK__SERVICETOKEN` только env/secret store; не из chat, Mini App, query, webhook body (ADR-013). ✅ docs + SecretScanner/`LooksLikeSecret`
- Encrypt-at-rest / AES-GCM seal ✅ (`EncryptedVkTokenStore`; scrub plaintext at startup). Same pattern as IG/Cursor.
- Official VK API only (`wall.get` / `utils.resolveScreenName`); Apify / HTML / `m.vk.com` forbidden.
- `Vk:ApiBaseUrl` только `api.vk.com` ✅ (`VkApiHostGuard` + ValidateOnStart; runtime normalize).
- CDN SSRF allowlist `*.userapi.com` ✅ (`VkCdnUrlGuard` + `IVkMediaDownloader`); download → `Research:ImageVolumePath` ✅ (`VkPhotoStore`); plan MediaPath relative only (`ResearchMediaPathGuard`); gateway media proxy (initData + path guard); no durable CDN URLs in Postgres.
- Allowlist communities in settings ✅ (`VkCommunitiesJson` / Mini App / `/research vk`); closed/Donut soft skip in client mapper; no comments / author profile scrape (PII); VK token rejected from UI/chat.
- Snapshot additive `source=vk` ✅; no raw token / CDN URLs in Postgres dumps/logs; cap ≤50 posts; photo download cap ≤14; allowlist ≤10.
- Hardening ✅: `Phase6HardeningTests` caps + non-goals (no scrape/Apify/user-OAuth/RAG/MinIO/Direct); README token rotation VK.
- Non-goals: user VK ID OAuth, MinIO, RAG/ES, Direct — out of Phase 6.

## Phase 7 controls (closed ✅)

- ADR-014: document RAG ≠ harness memory ≠ research artifacts; domain-split indexes (`kb-salon` / `kb-marketing`).
- Owner: `rag-service` + Elasticsearch; assistant-api does **not** query ES; HTTP + `X-Service-Key` only.
- Retriever via pack MCP (`salon`/`marketing` allowlist `kb-retriever` + skill) ✅; `_router`/`tasks` without RAG ✅ (`phase7-pack-retriever`).
- Secrets (`RAG__SERVICEKEY`, `ELASTICSEARCH__PASSWORD`): env/secret store only; never chat / Mini App / query / git. SecretScanner + LooksLikeSecret reject RAG/ES key names ✅.
- Soft-fail: empty/failed retrieval must not fail `/v1/chat` ✅; PII not in ES/query logs ✅ (status/domain/index only; no `{Query}`/`{Text}`/`{Detail}`).
- Embeddings: stub embedder ✅ (`phase7-rag-api`); no SaaS key.
- Caps ✅ (`RagLimits` / `RagClientLimits`): TopK≤20, text≤100k, query≤2k, snippet≤240, inject≤2400, metadata strip secrets.
- ES compose: xpack.security **on** + basic auth (`ELASTIC_PASSWORD` / `Elasticsearch__Username`+`Password`); HTTP SSL off (compose-internal); no published `:9200` ✅ (`phase7-hardening`).
- Non-goals this phase: MinIO, Яндекс Директ, Apify, mixing indexes, replacing harness/research with RAG — guard tests ✅.
- `phase7-docs` … `phase7-hardening` all ✅; Phase 7 closed.

## Phase 8 controls (compose ✅; presign later)

- ADR-015: MinIO **private** buckets + **presigned** PUT/GET; metadata+authz in assistant-api Postgres; no large byte proxy through API.
- Domain isolation: salon ≠ marketing prefixes/buckets; pack MCP files only in `salon`/`marketing`; `_router`/`tasks` without files MCP.
- Object keys: opaque server-generated; no PII / sequential ids; size + MIME allowlist before presign; short TTL.
- Secrets (`MINIO__ROOTUSER` / `MINIO__ROOTPASSWORD` / access keys): env/secret store; never chat / Mini App / query / git. Compose defaults are **dev-only**.
- Compose (`phase8-minio-compose` ✅): internal-only MinIO (no host `ports:`); `minio-init` sets `anonymous=none` on `tg-ai-salon` / `tg-ai-marketing`; no app wiring yet.
- Scanning hook stub in hardening; soft-fail missing MinIO must not fail `/v1/chat`.
- Non-goals this phase: Яндекс Директ, Apify, public bucket/CDN without TTL, research-volume full migration in early slices.
- Next: `phase8-presign` (metadata + short-TTL PUT/GET).

## Open Risks

- Реальный Telegram reply требует валидный bot token; placeholder даёт soft-fail 401 на sendMessage.
- Живой Cursor cloud no-repo path зависит от аккаунтных флагов Cursor; stub fallback закрывает compose без ключа.
- Internal HTTP to bridge carries decrypted key (compose trust model); harden with mTLS later if needed.
- Phase 7 closed. Phase 8 MinIO: docs ✅; compose ✅; presign/packs via remaining slices. Phase 9 Direct — только по явному запросу. ES volume created without auth must be wiped when enabling xpack first time.
- Mini App `TELEGRAM__WEBAPPURL` must be HTTPS publicly reachable for real Telegram clients.
- MinIO root password default in compose is **dev-only**; rotate via `MINIO__ROOTPASSWORD` before any shared env.


