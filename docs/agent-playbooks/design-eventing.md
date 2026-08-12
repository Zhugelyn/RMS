# Design Eventing Playbook

## Broker Choice

- RabbitMQ: command dispatch, competing consumers, routing keys, short-lived tasks.
- Kafka: event history, replay, stream processing, projections, high throughput append log.

## Message Design

```json
{
  "messageId": "uuid",
  "correlationId": "uuid",
  "causationId": "uuid",
  "schemaVersion": 1,
  "occurredAt": "2026-08-09T00:00:00Z",
  "producer": "service-name",
  "payload": {}
}
```

## Reliability

- Producer writes DB change and outbox row in one transaction.
- Outbox publisher sends and marks dispatched with retry.
- Consumer stores processed message ID before/with side effect.
- Poison messages go to DLQ/parking lot with enough context to debug.

## Compatibility

- Additive changes are safe.
- Rename/delete/type changes need new version.
- Consumers must ignore unknown fields.

