# Phase 6 webhook payload

Automation берёт **один** slice. Если payload пустой — `next_slice` из `memory/current-project.md` / `memory/phase-plan.md` (`phase6-vk-client`).

```json
{
  "slice": "phase6-vk-client",
  "task": "Один slice phase6-vk-client: IVkWallClient / HttpVkWallClient в assistant-api; VK__SERVICETOKEN из env/secret store + AES-GCM по паттерну IG; utils.resolveScreenName + wall.get; stub без токена; SSRF CDN allowlist *.userapi.com. Не scrape/Apify. Не artifacts/settings/Mini App/media persist (это следующие slices). schemaVersion не ломать. Token не из чата.",
  "acceptance": [
    "IVkWallClient + HttpVkWallClient + stub without token",
    "service token env/AES-GCM; reject from chat/Mini App",
    "resolveScreenName + wall.get mapping text + photo attachments",
    "SSRF allowlist *.userapi.com; no IP literals",
    "dotnet test green; no RAG/Apify/MinIO/Direct/user-OAuth",
    "run-log updated; next_slice=phase6-vk-artifacts"
  ]
}
```
