---
name: design-postgres-data
description: Designs PostgreSQL schemas, EF Core migrations, transaction boundaries, indexes, outbox, inbox, and data security. Use when changing service persistence or data consistency behavior.
---

# Design Postgres Data

## Инструкции

1. Прочитай `agents/postgres-agent.md`.
2. Определи service owner данных и запрети прямой доступ другим сервисам к таблицам.
3. Спроектируй schema, migrations, transaction boundaries, indexes и retention.
4. Для DB + broker consistency используй outbox/inbox.
5. Проверь PII, backup access, encryption needs и logging restrictions.

## Reference

См. `docs/agent-playbooks/design-postgres-data.md`.

