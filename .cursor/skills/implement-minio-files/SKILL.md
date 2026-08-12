---
name: implement-minio-files
description: Designs secure MinIO file upload, download, metadata, bucket policy, presigned URL, lifecycle, and scanning flows. Use when a service handles user files or object storage.
---

# Implement Minio Files

## Инструкции

1. Прочитай `agents/files-agent.md`.
2. Определи file owner service, metadata model, access rules и lifecycle.
3. Предпочитай presigned URLs для больших файлов; API хранит metadata и authorization.
4. Проверь size limits, MIME validation, key strategy, TTL, private buckets и scanning hook.
5. Обнови `memory/integration-contracts.md`, если появился file contract.

## Reference

См. `docs/agent-playbooks/implement-minio-files.md`.

