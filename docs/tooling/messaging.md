# Messaging Tooling

## RabbitMQ Checks

- Exchanges, queues and bindings exist as expected.
- Consumers ack/nack correctly.
- Retry queues and DLQ are configured.
- Messages include correlation id and schema version.

## Kafka Checks

- Topics have expected partitions and retention.
- Consumer groups are healthy.
- Lag is visible.
- Schema compatibility is enforced where schema registry exists.

## Failure Tests

- Consumer throws once: message is retried.
- Consumer always fails: message reaches DLQ/parking lot.
- Duplicate message: idempotency prevents duplicate side effect.
- Replay: consumer behavior is safe.

