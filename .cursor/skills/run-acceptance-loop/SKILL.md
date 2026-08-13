---
name: run-acceptance-loop
description: Runs a slice-based implement-test-fix loop until current acceptance criteria pass. Use in Cursor Automations or when the user asks to finish a task to acceptance, keep cycling, or close phase-plan checkboxes.
---

# Run Acceptance Loop

## Инструкции

1. Прочитай `memory/automation-memory-protocol.md`, `memory/current-project.md`, `memory/phase-plan.md`, `memory/run-log.md`.
2. Возьми один незакрытый slice текущей фазы. Не тащи следующую фазу.
3. Подними профильный skill/agent. Playbook — только один.
4. Реализуй, проверь, почини, повтори.
5. Security pass для Telegram, tokens, API, secrets.
6. Обнови repo memory и короткий Automation Memory.
7. Стоп на green criteria или blocker.

## Reference

См. `docs/agent-playbooks/acceptance-loop.md`.
