# Manage Agent Memory Playbook

## После каждого существенного run

1. `memory/run-log.md` — 5-8 строк.
2. `memory/phase-plan.md` — отметить сделанные checkbox.
3. `memory/service-catalog.md` / contracts / ADR — только если появилось устойчивое решение.
4. Automation Memory — 4-6 коротких фактов, см. `memory/automation-memory-protocol.md`.

## Чистка

- Если Memories разрослись: оставить phase/next/blocker, остальное вынести в repo memory и удалить из Memories.
- Не дублировать один и тот же ADR в Memories и в `architecture-decisions.md`. Repo побеждает.

## Контроль токенов

- Не прикладывать в контекст больше одного playbook.
- Не читать весь `docs/`.
- Не цитировать старые run-log целиком, только последний run.
