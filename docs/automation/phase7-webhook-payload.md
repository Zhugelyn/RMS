# Phase 7 webhook payload

Automation берёт **один** slice. Если payload пустой — `next_slice` из `memory/current-project.md` (`phase7-docs`).

```json
{
  "slice": "phase7-docs",
  "task": "Один slice phase7-docs: ADR-014 Knowledge/RAG (rag-service владеет ES; assistant-api не ходит в ES; индексы kb-salon / kb-marketing изолированы; retriever = pack MCP salon|marketing; не harness/research snapshot). README, catalog, contracts, security, .env.example placeholders. Без кода сервисов и без ES контейнера. schemaVersion не ломать. Не MinIO/Direct.",
  "acceptance": [
    "ADR-014 accepted in architecture-decisions.md",
    "phase-plan Phase 7 slices listed; MinIO=8 Direct=9",
    ".env.example placeholders only, no real secrets",
    "dotnet test still green (docs-only)",
    "run-log updated; next_slice=phase7-es-compose"
  ]
}
```
