# Secure Microservice Playbook

## Threat Model Минимум

1. Assets: данные, файлы, tokens, broker messages, admin operations.
2. Entry points: HTTP, broker consumers, file uploads, webhooks, admin jobs.
3. Trust boundaries: internet, ingress, cluster, broker, database, object storage.
4. Abuse cases: unauthorized read/write, replay, SSRF, path traversal, poison messages, data leak.

## Review Checklist

- Authn/authz enforced close to use case.
- Object-level authorization for files and tenant/user data.
- Input validation for DTO, message payloads, file metadata and URLs.
- Secrets never logged or committed.
- Logs avoid raw PII.
- Error responses do not leak internals.
- Container and Kubernetes manifests are hardened.

## Output Format

```markdown
## Findings

- Critical:
- High:
- Medium:
- Low:

## Fixes

- Concrete code/config changes.

## Residual Risk

- What remains and why.
```

