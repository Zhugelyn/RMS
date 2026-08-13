# Phase Plan

## Phase 1 — Shell

Acceptance:

- [ ] Два сервиса: `telegram-gateway`, `assistant-api`.
- [ ] `assistant-api` поднимает `POST /v1/chat` и health checks.
- [ ] `ILlmProvider` интерфейс; default = stub, без внешнего LLM.
- [ ] Telegram update -> assistant-api -> ответ в чат.
- [ ] Secrets не в git, не в логах, не в OpenAPI examples.
- [ ] Inter-service auth есть; Telegram token не уходит в assistant-api.
- [ ] Cursor API key не принимается из Telegram message.
- [ ] `dotnet test` проходит.
- [ ] ADR и contracts обновлены в `memory/`.

## Phase 2 — Cursor SDK runtime

- Подключить `@cursor/sdk` / `cursor-sdk` как один из `ILlmProvider`.
- Encrypted storage tenant/user Cursor API key, если ключ принадлежит клиенту.
- Harness loop: classify -> specialist agent -> verify.
- Resume по `agentId` для длинных задач.

## Phase 3 — Knowledge

- RAG service, embeddings, Elasticsearch.
- Отдельный ownership данных и retriever contract.

## Phase 4 — Files / Video

- MinIO, metadata DB, scanning hook, presigned URLs.
