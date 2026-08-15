# Current Project: Telegram AI Assistant

- Code name: `telegram-ai`
- Repo: `Zhugelyn/RMS`
- Branch: `main`
- Phase: `2-cursor-sdk` (acceptance closed 2026-08-15; next = Phase 3 Domain agent packs; Knowledge сдвинут на Phase 4)
- Language: русский
- Runtime: Docker Compose (`telegram-gateway` + `assistant-api` + internal `cursor-sdk-bridge`)

## Product North Star

ИИ-ассистент в Telegram для трёх доменов. Сам ИИ — отдельный сервис с harness-обвязкой. Клиентский UI — Telegram bot + Mini App. LLM runtime — Cursor SDK (у клиента уже есть подписка).

Домены:

- салон красоты: записи, расписание, клиенты;
- маркетинг: реклама, мониторинг, монетизация;
- повседневные задачи: планирование, встречи.

Позже: Яндекс Директ и другие внешние сервисы; генерация картинок; работа с фото и видео; RAG + Elasticsearch (можно готовые фреймворки).

## Goal Phase 2

Cursor SDK harness поверх Phase 1 shell.

1. `CursorSdkLlmProvider` + internal Node bridge `@cursor/sdk`.
2. Encrypt-at-rest Cursor API key (AES-GCM, master key из env).
3. Harness: classify → specialist → verify для `salon|marketing|tasks`.
4. Resume по `agentId`.
5. Stub = fallback без ключа / при ошибке SDK.
6. Без RAG/ES/MinIO/Директ.

Phase 2 specialist = persona-switch. Настоящие domain packs (AGENTS.md / skills / MCP / отдельный Agent на домен) + harness memory (profile + episodes) — **Phase 3**, не Knowledge.

## Phase 2 non-goals

- Elasticsearch, embeddings, RAG frameworks
- MinIO, генерация картинок, фото/видео пайплайн
- Яндекс Директ и прочие ads API
- Kubernetes / Nginx prod

## Token Budget Rules

- Один automation run = один slice из `memory/phase-plan.md`.
- Читать только релевантные `memory/*`, не все playbooks.
- Automation Memories = короткие указатели.
- Не генерировать будущие сервисы «на всякий случай».
