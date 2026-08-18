---
name: kb-retrieve
description: Retrieve salon (Babor) knowledge-base snippets via pack MCP kb-retriever / rag-service.
---

# KB retrieve (salon)

- Source: document RAG index `kb-salon` via rag-service (ADR-014). **Not** harness episodes, **not** IG/VK research.
- Runtime: assistant-api calls rag-service with this pack's domain only; hits are injected into the specialist prompt.
- Cross-domain search (`kb-marketing`) is forbidden.
- Empty hits → answer without KB; do not invent documents.
- Secrets / API keys are never in this skill or `mcp.json`.
