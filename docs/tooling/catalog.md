# Tooling Catalog

Каталог команд и инструментальных playbooks для будущих проектов. Harness использует его как справочник, а не как список команд для бездумного запуска.

## .NET

См. `docs/tooling/dotnet.md`.

- `dotnet build`
- `dotnet test`
- `dotnet format --verify-no-changes`
- EF Core migrations commands.

## Docker, Kubernetes, Nginx

См. `docs/tooling/platform.md`.

- `docker compose config`
- `docker compose up`
- `kubectl diff --server-side`
- `kubectl rollout status`
- `kubectl logs`

## Messaging

См. `docs/tooling/messaging.md`.

- RabbitMQ management UI/CLI checks.
- Kafka topic/consumer group checks.
- DLQ and retry inspection.

## MinIO

См. `docs/tooling/minio.md`.

- Bucket policy checks.
- Presigned URL flow checks.
- Metadata consistency checks.

## Memory / Automation

- Протокол: `memory/automation-memory-protocol.md`.
- Skills: `run-acceptance-loop`, `manage-agent-memory`, `build-telegram-ai-phase1`.

## Security

См. `docs/tooling/security.md`.

- Dependency scanning.
- Container scanning.
- Secret scanning.
- Kubernetes manifest policy checks.

