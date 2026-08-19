# Phase 8 webhook payload

Automation берёт **один** slice. Если payload пустой — `next_slice` из `memory/current-project.md` (`phase8-docs`).

```json
{
  "slice": "phase8-docs",
  "task": "Один slice phase8-docs: ADR-015 Files/MinIO (private buckets; presigned URLs; metadata+authz в assistant-api Postgres; байты не через API; pack MCP salon|marketing; domain prefix isolation). README, catalog, contracts, security, .env.example placeholders. Без кода сервисов и без MinIO контейнера. schemaVersion не ломать. Не Direct/Apify.",
  "acceptance": [
    "ADR-015 accepted in architecture-decisions.md",
    "phase-plan Phase 8 slices listed; Direct=9",
    ".env.example placeholders only, no real secrets",
    "dotnet test still green (docs-only)",
    "run-log updated; next_slice=phase8-minio-compose"
  ]
}
```
