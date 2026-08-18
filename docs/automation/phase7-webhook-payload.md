# Phase 7 webhook payload

Automation берёт **один** slice. Если payload пустой — `next_slice` из `memory/current-project.md` (`phase7-hardening`).

```json
{
  "slice": "phase7-hardening",
  "task": "Один slice phase7-hardening: caps, PII в логах, index isolation tests, token notes, non-goals guard (no MinIO/Direct/Apify; no mixing indexes). schemaVersion не ломать.",
  "acceptance": [
    "caps + PII/log hygiene for rag path",
    "index isolation tests remain green",
    "token rotation notes; non-goals guard",
    "dotnet test green; no MinIO/Direct/Apify",
    "Phase 7 closed; run-log updated"
  ]
}
```

Previous (done):

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
