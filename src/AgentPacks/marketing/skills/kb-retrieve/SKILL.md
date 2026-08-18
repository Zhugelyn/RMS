---
name: kb-retrieve
description: Retrieve beauty-market knowledge-base snippets via pack MCP kb-retriever / rag-service.
---

# KB retrieve (marketing)

- Source: document RAG index `kb-marketing` via rag-service (ADR-014). **Not** harness episodes, **not** IG/VK research snapshots.
- Runtime: assistant-api calls rag-service with this pack's domain only; hits are injected into the specialist prompt.
- Cross-domain search (`kb-salon`) is forbidden.
- Empty hits → answer without KB; mark hypotheses when inventing market claims.
- Secrets / API keys are never in this skill or `mcp.json`.
