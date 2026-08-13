# Current Project: Telegram AI Assistant

- Code name: `telegram-ai`
- Repo: `Zhugelyn/RMS`
- Branch: `main`
- Phase: `1-shell`
- Language: русский

## Goal Phase 1

Собрать рабочую оболочку, не RAG и не полноценный LLM harness.

1. `telegram-gateway` — Telegram bot client.
2. `assistant-api` — расширяемый AI assistant server.
3. Контракт `POST /v1/chat` с versioning.
4. `ILlmProvider` как точка расширения. Phase 1 = stub/echo provider.
5. Secrets: Telegram bot token и будущий Cursor API key только в encrypted secret store.
6. Не реализовывать RAG, embeddings, Elasticsearch, files, video.

## Phase 2+ reserved

- Cursor SDK harness (`Agent.create` / `send` / `resume`) как LLM runtime.
- RAG service, embedding service, Elasticsearch.
- Files/video через MinIO.
- Task-specific harness agent поверх assistant-api.

## Token Budget Rules

- Один automation run = один slice из `memory/phase-plan.md`.
- Читать только релевантные `memory/*` файлы, не все playbooks сразу.
- Automation Memories хранят только короткие факты, не архитектуру целиком.
- Не генерировать будущие сервисы "на всякий случай".
