# Phase 1 webhook payload

Automation должна брать задачу из этого JSON. Если payload пустой — один checkbox из `memory/phase-plan.md`.

```json
{
  "slice": "phase1-shell",
  "task": "Зафиксируй ТЗ в memory и реализуй Phase 1: Docker, telegram-gateway + Mini App, assistant-api stub. Не RAG/ES/SDK/Директ.",
  "acceptance": [
    "docker compose up поднимает два сервиса",
    "POST /v1/chat и health работают",
    "ILlmProvider stub",
    "Mini App с меню салон/маркетинг/задачи",
    "секреты не в git и не в логах",
    "dotnet test зелёный",
    "memory/phase-plan и run-log обновлены"
  ]
}
```
