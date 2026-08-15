# Agent packs

Source of truth for Phase 3 domain specialists. Owner: `assistant-api`. Runtime execution (later slices): `cursor-sdk-bridge`.

## Layout

```
AgentPacks/
  pack.schema.json
  salon|marketing|tasks|_router/
    AGENTS.md
    skills/
    prompts/
    mcp.json      # allowlist only; no secrets
    pack.json     # model, effort, runtime, resumePolicy, answersUser
```

## Rules

- `_router`: `answersUser=false`, `resumePolicy=none`.
- Specialists: `answersUser=true`, `resumePolicy=per-domain-conversation`.
- MCP servers empty until a later slice wires real tools.
- Do not put API keys in `mcp.json`.
