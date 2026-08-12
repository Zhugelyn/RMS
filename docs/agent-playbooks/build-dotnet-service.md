# Build Dotnet Service Playbook

## Endpoint Checklist

- Route and HTTP method.
- Auth policy.
- Request DTO and validation.
- Response DTO.
- ProblemDetails errors.
- CancellationToken.
- Logging with correlation id.
- Metrics for latency/errors if production path.

## Service Internals

- Keep domain/application logic outside controllers where project style allows it.
- Use interfaces for external adapters: DB, broker, object storage, HTTP clients.
- Typed options for config; validate options on startup.
- Avoid static service locators and hidden global state.

## Integration Checklist

- PostgreSQL: migration, transaction boundary, indexes.
- RabbitMQ/Kafka: outbox/inbox, idempotency, retry/DLQ.
- MinIO: metadata ownership, size limits, presigned URLs.
- Kubernetes: health checks and graceful shutdown.

## Verification

```powershell
dotnet test
dotnet format --verify-no-changes
```

Use project-specific commands when they exist.

