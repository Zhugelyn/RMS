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
- `Cursor:ApiKey` принадлежит только `assistant-api` (Phase 2+). Phase 1 ключ не обязателен.
- Не принимать токены из chat message, query, filename, webhook body пользователя.
- Webhook: проверять Telegram secret token header.
- Inter-service: отдельный service credential, не bot token.
- Encrypt-at-rest для клиентских ключей: ASP.NET Data Protection или AES-GCM. Master key = env/K8s Secret/user-secrets.
- Логи: никогда bot token, Cursor key, Authorization, raw Telegram update с токенами.
- Rotate: смена secret не требует смены API contract.

## Phase 1 controls (implemented)

- `.env` gitignored; `.env.example` без реальных значений.
- `X-Service-Key` на `assistant-api` (health публичный).
- Gateway `SecretScanner` + assistant reject secret-like chat text.
- Telegram HttpClient logging disabled (`ITelegramBotClient` filter) — token в path не логируется.
- Containers: non-root uid 10001, healthchecks, no secrets in images.
- Nested Docker: `scripts/compose-up.sh` включает `ip_forward`/`br_netfilter` для bridge DNS.

## Open Risks

- Реальный Telegram reply требует валидный bot token; placeholder даёт soft-fail 401 на sendMessage.
- Cursor SDK runtime, RAG/ES, медиа и Яндекс Директ отложены. Не тащить их секреты в Phase 1.
- Mini App initData auth ещё не enforced (Phase 1 anonymous mini user ok).


