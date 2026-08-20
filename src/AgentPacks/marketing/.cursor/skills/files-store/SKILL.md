---
name: files-store
description: List/use marketing-domain MinIO files via pack MCP files (assistant-api presign + metadata).
---

# Files store (marketing)

- Source: private MinIO bucket `tg-ai-marketing` via assistant-api metadata + short-TTL presigned PUT/GET (ADR-015).
- Runtime: assistant-api injects recent file metadata for this pack's domain only; bytes never go through the agent or chat.
- Cross-domain access (`salon` bucket / other users) is forbidden.
- Upload/download: client uses `/v1/files/upload-intent` → PUT to MinIO → confirm; download via `/v1/files/{id}/download-url`. Do not invent URLs or proxy bytes.
- Empty list → answer without files; do not invent fileIds.
- Secrets / MinIO keys are never in this skill or `mcp.json`.
