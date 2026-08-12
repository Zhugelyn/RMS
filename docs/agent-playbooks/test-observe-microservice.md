# Test Observe Microservice Playbook

## Test Selection

- Pure business rule: unit test.
- DB query/migration: integration test with PostgreSQL.
- Broker publish/consume: integration test with RabbitMQ/Kafka and idempotency assertion.
- API contract: contract test or snapshot of OpenAPI where project supports it.
- Multi-service happy path: narrow e2e test.

## Observability Fields

- `traceId`
- `correlationId`
- `causationId`
- `messageId`
- `userId` or `tenantId` only when allowed and safe
- `service`
- `operation`
- `durationMs`
- `outcome`

## Production Readiness

- Health checks are meaningful.
- Metrics cover latency, errors, retries, queue lag and DLQ.
- Logs explain failures without leaking sensitive data.
- Traces cross HTTP and broker boundaries.

