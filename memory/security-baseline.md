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
- GenerateImage через Cursor + local marketing-pack volume; без отдельного OpenAI Images secret (ADR-011) — later slice.
- Mini App research settings без IG token в браузере; `/research` не принимает токены в тексте — later slices.
- Volume path: no path traversal; size limits on generated images before Telegram send.
- Postgres: credentials только env (`POSTGRES__PASSWORD` / `ConnectionStrings__AssistantDb`); migrations без secrets in git; database-per-service owner=assistant-api; ready fails if CS set but DB down; without CS → in-process fallback.


## Open Risks

- Реальный Telegram reply требует валидный bot token; placeholder даёт soft-fail 401 на sendMessage.
- Живой Cursor cloud no-repo path зависит от аккаунтных флагов Cursor; stub fallback закрывает compose без ключа.
- Mini App initData auth ещё не enforced.
- Internal HTTP to bridge carries decrypted key (compose trust model); harden with mTLS later if needed.
- Phase 4: Graph token expiry / rate limits (soft-handled); GenerateImage availability; scheduler/Mini App `/research` not wired yet (`phase4-scheduler`).


