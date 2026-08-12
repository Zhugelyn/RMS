# Security Baseline

## Обязательный минимум

- Authn/authz для public API, privileged operations, consumers команд и file operations.
- Secrets только через конфигурационные providers, Kubernetes Secrets или external secret provider.
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

## Open Risks

- Конкретные риски появятся после первого проекта.

