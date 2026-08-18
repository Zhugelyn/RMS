# Phase 6 webhook payload

Automation берёт **один** slice. Если payload пустой — `next_slice` из `memory/current-project.md` / `memory/phase-plan.md`.

```json
{
  "slice": "phase6-vk-docs",
  "task": "Зафиксируй Phase 6 VK Public Research в memory: ADR-013 (официальный VK API wall.get открытых пабликов, service token, allowlist screen_name; не scrape/Apify/HTML). README, catalog, contracts, security, .env.example VK__SERVICETOKEN=. Без кода сервисов. schemaVersion не ломать. Один slice.",
  "acceptance": [
    "ADR-013 accepted in memory/architecture-decisions.md",
    "phase-plan Phase 6 slices listed; RAG=7 Files=8 Direct=9",
    ".env.example has VK__SERVICETOKEN= placeholder, no real secret",
    "security-baseline: VK token not from chat; CDN SSRF note reserved for client slice",
    "dotnet test still green (docs-only)",
    "run-log updated; next_slice=phase6-vk-client"
  ]
}
```
