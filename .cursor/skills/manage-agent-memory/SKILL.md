---
name: manage-agent-memory
description: Manages repo memory and Cursor Automation Memories to persist work context and keep token usage low. Use after agent/automation runs or when the user asks about Memory, context, or token control.
---

# Manage Agent Memory

## Инструкции

1. Repo memory = source of truth. Automation Memories = короткие указатели.
2. Читай протокол `memory/automation-memory-protocol.md`.
3. Пиши в Memories только phase, last_slice, next_slice, blocker, список файлов для чтения.
4. Не клади secrets и простыни кода в Memories.
5. После run обнови `memory/run-log.md` и checkbox в `memory/phase-plan.md`.

## Reference

См. `docs/agent-playbooks/manage-agent-memory.md`.
