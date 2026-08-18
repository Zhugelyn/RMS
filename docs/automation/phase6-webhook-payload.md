# Phase 6 webhook payload

Phase 6 **closed** (`phase6-vk-hardening` ✅). Empty webhook → no open Phase 6 slice; do not start Phase 7 RAG / deferred `phase5-ui-hardening` without explicit slice.

Historical last slice payload:

```json
{
  "slice": "phase6-vk-hardening",
  "task": "Один slice phase6-vk-hardening: caps, ApiBaseUrl=api.vk.com, MediaPath relative-only, token rotation notes, non-goals guard (no scrape/Apify/user-OAuth/RAG/MinIO/Direct). schemaVersion не ломать.",
  "acceptance": [
    "VkApiHostGuard + ResearchMediaPathGuard wired; caps explicit",
    "Phase6HardeningTests green; non-goals not wired",
    "dotnet test green; README token rotation VK",
    "run-log updated; Phase 6 closed; next_slice=none"
  ]
}
```
