# Phase 6 webhook payload

Automation берёт **один** slice. Если payload пустой — `next_slice` из `memory/current-project.md` / `memory/phase-plan.md` (`phase6-vk-media`).

```json
{
  "slice": "phase6-vk-media",
  "task": "Один slice phase6-vk-media: скачать photo sizes с CDN *.userapi.com в research volume; media proxy; не хранить CDN URL как долгоживущие. schemaVersion не ломать. Не hardening.",
  "acceptance": [
    "VK photo attachments downloaded to volume via SSRF allowlist",
    "media proxy serves local paths; CDN URLs not persisted long-term",
    "dotnet test green; no RAG/Apify/MinIO/Direct/user-OAuth",
    "run-log updated; next_slice=phase6-vk-hardening"
  ]
}
```
