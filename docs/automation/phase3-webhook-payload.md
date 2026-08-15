# Phase 3 webhook payload

Automation / Cloud Agent harness. **Один run = один slice.** Не Phase 3 целиком.

Если payload пустой — взять первый незакрытый slice из `memory/phase-plan.md` Phase 3.

## Slice 1 (стартовый)

```json
{
  "slice": "phase3-pack-layout",
  "task": "Только каталог src/AgentPacks/{salon,marketing,tasks,_router} + pack.json schema + PackCatalog loader/validator. Runtime chat/bridge/DomainHarness не менять. Не RAG, не Postgres memory, не MCP wiring.",
  "acceptance": [
    "есть packs salon, marketing, tasks, _router с AGENTS.md, skills/, prompts/, mcp.json, pack.json",
    "mcp.json без секретов, allowlist пустой",
    "_router answersUser=false, resumePolicy=none; остальные answersUser=true, resumePolicy=per-domain",
    "dotnet test зелёный",
    "POST /v1/chat поведение Phase 2 не сломано (stub/cursor-sdk как было)",
    "memory/run-log обновлён; checkbox slice не закрывает всю Phase 3"
  ]
}
```

Дальше, отдельными run:

- `phase3-bridge-pack-runtime`
- `phase3-router-and-affinity`
- `phase3-verify-isolation`
- `phase3-harness-memory`

Агенты: `agents/harness-agent.md` + `run-acceptance-loop`. Код: `agents/ai-assistant-agent.md`.
