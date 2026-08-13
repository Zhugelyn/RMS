# Automation Memory Protocol

Есть два слоя памяти. Не смешивать.

## 1. Repo memory = source of truth

Читать и писать файлы в `memory/`. Они в git, ревьюятся, дёшевы для следующего агента.

| Файл | Когда читать | Когда писать |
| --- | --- | --- |
| `current-project.md` | каждый run | смена фазы/цели |
| `phase-plan.md` | каждый run | закрытие checkbox |
| `run-log.md` | каждый run | конец run, 5-8 строк |
| `service-catalog.md` | архитектура/код сервиса | новый сервис |
| `architecture-decisions.md` | спор/новое решение | ADR |
| `integration-contracts.md` | API/events | новый контракт |
| `security-baseline.md` | secrets/auth/files | новый control/риск |
| `project-principles.md` | редко | смена принципов |

Не читать все `docs/agent-playbooks/*` сразу. Только playbook выбранного skill.

## 2. Automation Memories = указатели

Инструмент Memories в Cursor Automation. Живёт между запусками, но дорогой и шумный, если туда сваливать простыни.

Писать туда только:

- `project=telegram-ai phase=1-shell`
- `last_slice=<id> status=<passed|blocked>`
- `next_slice=<id>`
- `blocker=<one line or none>`
- `read=memory/current-project.md,memory/phase-plan.md,memory/run-log.md`

Запрещено писать в Memories:

- secrets, tokens, keys, connection strings;
- полный ADR, полный код, большие логи;
- будущий RAG/ES дизайн, если фаза 1;
- сырой user prompt целиком, если он длинный.

## Token control

1. Сначала Memories (если есть) -> потом 3 файла: current-project, phase-plan, run-log.
2. Дальше только файлы нужного агента/skill.
3. Один run = один slice. Не закрывать всю Phase 1, если slice меньше.
4. Если slice done — обновить checkbox, run-log, Memories. Не пересказывать архитектуру заново.
5. Если blocked — одна строка blocker + что нужно от человека. Стоп.
6. Не открывать PR с "ещё и RAG, раз уж начали".
