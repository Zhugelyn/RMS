# Phase 7 webhook payload

Automation берёт **один** slice. Если payload пустой — `next_slice` из `memory/current-project.md` (`phase7-pack-retriever`).

```json
{
  "slice": "phase7-pack-retriever",
  "task": "Один slice phase7-pack-retriever: MCP/skill retriever в salon и marketing packs; inject hits в specialist; router/tasks без RAG. assistant-api → rag-service HTTP (X-Service-Key), soft-fail empty hits. schemaVersion не ломать. Не MinIO/Direct/Apify. Не hardening (→ phase7-hardening).",
  "acceptance": [
    "salon/marketing packs call rag-service search for their domain only",
    "router/tasks have no RAG MCP",
    "soft-fail empty hits does not break /v1/chat",
    "dotnet test green; no MinIO/Direct/Apify",
    "run-log updated; next_slice=phase7-hardening"
  ]
}
```

Previous (done):

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
