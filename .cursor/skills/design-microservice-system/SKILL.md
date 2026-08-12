---
name: design-microservice-system
description: Designs microservice architecture, service boundaries, contracts, ADRs, and consistency models. Use when planning a new system, decomposing a domain, choosing sync or async integration, or reviewing microservice boundaries.
---

# Design Microservice System

## Инструкции

1. Прочитай `memory/project-principles.md`, `memory/service-catalog.md` и `memory/architecture-decisions.md`.
2. Используй `agents/architecture-agent.md`.
3. Сначала проверь, нужен ли микросервис, или modular monolith пока честнее.
4. Зафиксируй service ownership, database ownership, API/event/file contracts и consistency model.
5. Для cross-service workflows требуй outbox/inbox, idempotency и failure handling.
6. Для устойчивых решений подготовь ADR update.

## Reference

См. `docs/agent-playbooks/design-microservice-system.md`.

