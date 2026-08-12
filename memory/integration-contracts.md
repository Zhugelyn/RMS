# Integration Contracts

Храни здесь устойчивые REST/gRPC/event/file contracts между сервисами.

## REST/gRPC Contract Template

```markdown
## API: <service>.<operation>

- Owner:
- Consumers:
- Method/path or RPC:
- Request:
- Response:
- Errors:
- Auth:
- Rate/size limits:
- Backward compatibility:
```

## Event Contract Template

```markdown
## Event: <name>

- Owner:
- Broker: RabbitMQ | Kafka
- Version:
- Schema:
- Producer:
- Consumers:
- Correlation/causation:
- Idempotency key:
- Retry/DLQ:
- Compatibility policy:
```

## File Contract Template

```markdown
## File Flow: <name>

- Owner:
- Bucket:
- Object key strategy:
- Metadata:
- Upload:
- Download:
- Access control:
- Retention:
- Scanning:
```

