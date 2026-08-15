# Current Project: Telegram AI Assistant

- Code name: `telegram-ai`
- Repo: `Zhugelyn/RMS`
- Branch: `main`
- Phase: `3-domain-agent-packs` (in progress; slice `phase3-pack-layout` done 2026-08-15; Phase 2 closed)
- Language: русский
- Runtime: Docker Compose (`telegram-gateway` + `assistant-api` + internal `cursor-sdk-bridge`)

## Product North Star

ИИ-ассистент в Telegram для трёх доменов. Сам ИИ — отдельный сервис с harness-обвязкой. Клиентский UI — Telegram bot + Mini App. LLM runtime — Cursor SDK (у клиента уже есть подписка).

Домены:

- салон красоты: записи, расписание, клиенты;
- маркетинг: реклама, мониторинг, монетизация;
- повседневные задачи: планирование, встречи.

Позже: Яндекс Директ и другие внешние сервисы; генерация картинок; работа с фото и видео; RAG + Elasticsearch (можно готовые фреймворки).

## Goal Phase 3 (current)

Domain agent packs поверх Phase 2 harness.

1. Каталог `src/AgentPacks/{salon,marketing,tasks,_router}` + `pack.json` schema + `PackCatalog` (slice `phase3-pack-layout` ✅).
2. Bridge pack runtime (cwd/skills/MCP/model).
3. Router pack + agentId affinity per domain.
4. Hard verify + isolation.
5. Harness memory (profile + episodes).

Chat/bridge/DomainHarness runtime path ещё Phase 2 до следующих slices.

## Phase 3 non-goals

- Elasticsearch, embeddings, RAG frameworks
- MinIO, генерация картинок, фото/видео пайплайн
- Яндекс Директ и прочие ads API
- Kubernetes / Nginx prod

## Token Budget Rules

- Один automation run = один slice из `memory/phase-plan.md`.
- Читать только релевантные `memory/*`, не все playbooks.
- Automation Memories = короткие указатели.
- Не генерировать будущие сервисы «на всякий случай».
