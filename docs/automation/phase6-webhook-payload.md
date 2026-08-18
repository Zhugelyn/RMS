# Phase 6 webhook payload

Automation берёт **один** slice. Если payload пустой — `next_slice` из `memory/current-project.md` / `memory/phase-plan.md` (`phase6-vk-artifacts`).

```json
{
  "slice": "phase6-vk-artifacts",
  "task": "Один slice phase6-vk-artifacts: map VK wall items → research snapshot (source=vk additive); inject marketing pack; cap ≤50; soft-fail. Не settings/Mini App/media persist. schemaVersion не ломать. Token не из чата.",
  "acceptance": [
    "VK wall → snapshot source=vk additive; schemaVersion unchanged",
    "marketing inject latest VK snapshot; salon isolation",
    "cap ≤50; soft-fail persist/inject",
    "dotnet test green; no RAG/Apify/MinIO/Direct/user-OAuth",
    "run-log updated; next_slice=phase6-vk-settings"
  ]
}
```
