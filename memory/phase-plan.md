# Phase Plan

## Phase 1 — Shell

Acceptance:

- [x] `docker compose up` поднимает `telegram-gateway` и `assistant-api`.
- [x] `assistant-api`: `POST /v1/chat`, `GET /health/live`, `GET /health/ready`.
- [x] `ILlmProvider` интерфейс; default = stub, без внешнего LLM.
- [x] Telegram chat: update -> assistant-api -> ответ.
- [x] Mini App: экраны салон / маркетинг / задачи; кнопки шлют `/v1/chat` с `intent`; без реальной бизнес-логики.
- [x] Secrets из env/Docker secrets, не в git, не в логах, не в OpenAPI examples.
- [x] Inter-service auth есть; Telegram token не уходит в assistant-api.
- [x] Cursor API key не принимается из Telegram message.
- [x] `dotnet test` проходит.
- [x] ADR, catalog, contracts, run-log обновлены в `memory/`.

## Phase 2 — Cursor SDK harness

- `@cursor/sdk` / `cursor-sdk` как `ILlmProvider`.
- Encrypted storage клиентского Cursor API key.
- Harness под домены: маркетинг, салон, повседневные задачи.
- Classify -> specialist agent -> verify. Resume по `agentId`.
- Оптимизация токенов: короткий контекст, repo memory, не тащить весь RAG.

## Phase 3 — Knowledge

- RAG service + embeddings + Elasticsearch. Можно готовые фреймворки.
- Отдельный ownership данных и retriever contract.
- Не смешивать индекс салона и маркетинга без явного решения.

## Phase 4 — Files / Video / Images

- MinIO, metadata DB, scanning hook, presigned URLs.
- Генерация картинок и работа с фото/видео через отдельные adapters.
- Большие файлы не проксировать через assistant-api без причины.

## Phase 5 — External tools

- Яндекс Директ и другие ads/CRM integrations.
- Отдельные tool adapters, секреты per-integration, least privilege.
