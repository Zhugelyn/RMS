# Phase 6 webhook payload

Automation берёт **один** slice. Если payload пустой — `next_slice` из `memory/current-project.md` / `memory/phase-plan.md` (`phase6-vk-settings`).

```json
{
  "slice": "phase6-vk-settings",
  "task": "Один slice phase6-vk-settings: allowlist пабликов (screen_name / owner_id) в research settings; Mini App + /research аддитивно; не принимать VK token из UI/чата. schemaVersion не ломать. Не media persist.",
  "acceptance": [
    "settings allowlist screen_name/owner_id additive; no token from UI",
    "Mini App + /research аддитивно (не ломать IG)",
    "capture uses allowlist targets",
    "dotnet test green; no RAG/Apify/MinIO/Direct/user-OAuth",
    "run-log updated; next_slice=phase6-vk-media"
  ]
}
```
