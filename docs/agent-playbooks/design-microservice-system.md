# Design Microservice System Playbook

## Быстрый алгоритм

1. Сформулируй business capability и users/jobs-to-be-done.
2. Нарисуй candidate contexts: где меняются данные, правила и команда-владелец.
3. Для каждого context ответь:
   - какие данные он owns;
   - какие операции выполняет;
   - какие события публикует;
   - какие чужие данные ему нужны;
   - какой failure mode приемлем.
4. Проверь, не лучше ли начать с modular monolith.
5. Если микросервис оправдан, зафиксируй API/event/file contracts.

## Хороший service boundary

- Имеет собственную модель данных.
- Может деплоиться независимо.
- Не требует distributed transaction для основной операции.
- Имеет понятный owner и lifecycle.
- Может деградировать при недоступности зависимостей.

## Красные флаги

- "Сервис" является CRUD-таблицей, а не business capability.
- Несколько сервисов пишут в одну таблицу.
- Каждый request делает цепочку из 4-5 синхронных вызовов.
- Event payload становится dump всей database entity.
- Security/observability не описаны в контракте.

## ADR Output

```markdown
## ADR-XXX: <decision>

- Context:
- Decision:
- Consequences:
- Alternatives:
- Security impact:
```

