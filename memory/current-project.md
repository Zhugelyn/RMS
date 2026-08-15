# Current Project: Telegram AI Assistant

- Code name: `telegram-ai`
- Repo: `Zhugelyn/RMS`
- Branch: `main`
- Phase: `3-domain-agent-packs` (acceptance closed functionally 2026-08-15; leftover = Postgres durable memory)
- Language: русский
- Runtime: Docker Compose (`telegram-gateway` + `assistant-api` + internal `cursor-sdk-bridge` + AgentPacks)

## Product North Star

ИИ-ассистент в Telegram для трёх доменов. Сам ИИ — отдельный сервис с harness-обвязкой. Клиентский UI — Telegram bot + Mini App. LLM runtime — Cursor SDK (у клиента уже есть подписка).

Домены:

- **salon / Babor (Брянск)**: развивать салон — идеи, удержание, локальный маркетинг ради салона; harness memory фактов о салоне;
- **marketing**: рынок красоты, топ-бренды косметики, тренды, таргет/аудитории;
- **tasks**: расписание работ и напоминания.

Позже: Яндекс Директ и другие внешние сервисы; генерация картинок; работа с фото и видео; RAG + Elasticsearch (можно готовые фреймворки).

## Goal Phase 3 (done functionally)

Domain agent packs + live harness:

1. Packs on disk + PackCatalog ✅
2. Bridge local pack runtime ✅
3. Router + per-domain affinity ✅
4. Hard verify + isolation ✅
5. Harness memory inject/write (in-memory) ✅

## Phase 3 non-goals / leftover

- Elasticsearch, embeddings, RAG frameworks
- MinIO, генерация картинок, фото/видео пайплайн
- Яндекс Директ и прочие ads API
- Kubernetes / Nginx prod
- Postgres durable harness memory (interface ready; in-process store now)

## Token Budget Rules

- Один automation run = один slice из `memory/phase-plan.md`.
- Читать только релевантные `memory/*`, не все playbooks.
- Automation Memories = короткие указатели.
- Не генерировать будущие сервисы «на всякий случай».
