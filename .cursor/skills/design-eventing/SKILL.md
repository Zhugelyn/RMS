---
name: design-eventing
description: Designs RabbitMQ and Kafka messaging contracts, retries, idempotency, DLQ, schema versioning, outbox, inbox, and observability. Use when adding or reviewing asynchronous communication.
---

# Design Eventing

## Инструкции

1. Прочитай `agents/messaging-agent.md` и `memory/integration-contracts.md`.
2. Выбери RabbitMQ для commands/work queues/routing, Kafka для durable event log/replay/streams.
3. Для каждого message зафиксируй owner, schema, version, correlation, idempotency и compatibility.
4. Опиши retry, DLQ/parking lot, replay behavior и ordering assumptions.
5. Проверь consumer idempotency и outbox/inbox.

## Reference

См. `docs/agent-playbooks/design-eventing.md`.

