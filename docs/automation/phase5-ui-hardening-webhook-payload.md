# phase5-ui-hardening webhook payload

Automation берёт **один** slice. Если payload пустой — `next_slice` из `memory/current-project.md` (`phase5-ui-hardening`).

Phase 6 closed. **Не** открывать Phase 7 RAG / ES / embeddings.

```json
{
  "slice": "phase5-ui-hardening",
  "task": "Один slice phase5-ui-hardening: polish/a11y/limits/edge cases Research Studio Mini App (IG + VK allowlist/gallery). Не RAG/ES/embeddings/MinIO/Direct. schemaVersion не ломать.",
  "acceptance": [
    "Studio polish: empty/error/limits/a11y edge cases",
    "VK allowlist + gallery paths covered if already in UI",
    "dotnet test green; no RAG/Apify/MinIO/Direct wiring",
    "run-log updated; Phase 5 leftover closed or explicit leftover noted"
  ]
}
```
