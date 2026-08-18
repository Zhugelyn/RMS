# Phase 7 webhook payload

Automation берёт **один** slice. Если payload пустой — `next_slice` из `memory/current-project.md` (`phase7-rag-api`).

```json
{
  "slice": "phase7-rag-api",
  "task": "Один slice phase7-rag-api: rag-service ingest/search поверх compose Elasticsearch; domain isolation kb-salon/kb-marketing; stub или embedder из ADR-014; auth X-Service-Key. assistant-api не ходит в ES напрямую. schemaVersion не ломать. Не MinIO/Direct/Apify. Не pack retriever (→ phase7-pack-retriever).",
  "acceptance": [
    "rag-service in compose depends on healthy elasticsearch",
    "ingest + search HTTP with X-Service-Key",
    "cross-domain search rejected (test)",
    "dotnet test green; no MinIO/Direct/Apify",
    "run-log updated; next_slice=phase7-pack-retriever"
  ]
}
```

Previous (done):

```json
{
  "slice": "phase7-es-compose",
  "task": "Elasticsearch в Docker Compose; health; без app wiring.",
  "acceptance": [
    "elasticsearch service + healthcheck + internal :9200",
    "no rag-service / no assistant-api ES wiring",
    "dotnet test green",
    "next_slice=phase7-rag-api"
  ]
}
```
