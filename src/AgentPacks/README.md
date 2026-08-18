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
- MCP: `salon` / `marketing` allowlist `kb-retriever` only (Phase 7 RAG inject via assistant-api → rag-service). `_router` / `tasks` stay empty.
- Do not put API keys in `mcp.json`.
